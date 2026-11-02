namespace NumoSql.Storage;

/// <summary>
/// Append-only log of committed transactions. Append returns only after the record is flushed to disk, so a
/// commit is durable before the caller publishes it. Opening the file takes an exclusive lock, so a second
/// process cannot write the same database.
/// </summary>
internal sealed class WriteAheadLog : IDisposable
{
    private const int ZeroScanChunkLength = 1 << 20;

    private readonly FileStream stream;
    private bool isBroken;

    public WriteAheadLog(string path)
    {
        stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    /// <summary>
    /// Applies every complete record in order, then discards an incomplete record at the end of the file. That
    /// record was never acknowledged as committed, so discarding it loses nothing a caller was told was saved.
    /// </summary>
    public void Replay(Action<WalRecord> apply)
    {
        var fileLength = stream.Length;
        var offset = 0L;

        while (ReadFrame(offset, fileLength) is { } frame)
        {
            apply(WalFrame.Decode(frame.Payload));
            offset = frame.EndOffset;
        }

        if (offset < fileLength)
        {
            stream.SetLength(offset);
            stream.Flush(true);
        }
    }

    public void Append(WalRecord record)
    {
        ThrowIfBroken();
        var frame = WalFrame.Encode(record);
        var previousLength = stream.Length;

        try
        {
            stream.Position = previousLength;
            stream.Write(frame);
            stream.Flush(true);
        }
        catch (IOException writeFailure)
        {
            isBroken = !TryRestoreLength(previousLength);
            throw new NumoSqlException("commit failed: the write-ahead log could not be written", writeFailure);
        }
    }

    /// <summary>Empties the log. Only call this after a checkpoint has captured every committed record.</summary>
    public void Truncate()
    {
        stream.SetLength(0);
        stream.Flush(true);
        isBroken = false;
    }

    public void Dispose()
    {
        stream.Dispose();
    }

    /// <summary>Returns null for an incomplete record at the end of the file.</summary>
    private FrameRead? ReadFrame(long offset, long fileLength)
    {
        if (fileLength - offset < WalFrame.HeaderLength)
        {
            return null;
        }

        var header = WalFrame.ParseHeader(ReadBytes(offset, WalFrame.HeaderLength));
        if (!header.IsIntact)
        {
            // With no intact record after it, this is an unfinished append, which a crash can leave partly written
            // or zero-filled. Intact data after it means the damage is to committed records.
            if (IsZeroFilledFrom(offset, fileLength) || !ContainsIntactFrameAfter(offset, fileLength))
            {
                return null;
            }

            throw new DatabaseCorruptException($"write-ahead log has a damaged record header at byte {offset}");
        }

        if (!WalFrame.IsValidPayloadLength(header.PayloadLength))
        {
            throw new DatabaseCorruptException($"write-ahead log record at byte {offset} has an impossible length");
        }

        var frameEnd = offset + WalFrame.HeaderLength + header.PayloadLength;
        if (frameEnd > fileLength)
        {
            return null;
        }

        var payload = ReadBytes(offset + WalFrame.HeaderLength, header.PayloadLength);
        if (Crc32.Compute(payload) == header.PayloadChecksum)
        {
            return new FrameRead(payload, frameEnd);
        }

        // A bad checksum on the final record is a write that was cut short. Anywhere else it is real damage.
        if (frameEnd == fileLength)
        {
            return null;
        }

        throw new DatabaseCorruptException($"write-ahead log record at byte {offset} fails its checksum");
    }

    /// <summary>Looks for a complete, checksummed record after a damaged header. Runs only on the error path.</summary>
    private bool ContainsIntactFrameAfter(long damagedOffset, long fileLength)
    {
        for (var candidate = damagedOffset + 1; candidate + WalFrame.HeaderLength <= fileLength; candidate++)
        {
            var header = WalFrame.ParseHeader(ReadBytes(candidate, WalFrame.HeaderLength));
            if (!header.IsIntact || !WalFrame.IsValidPayloadLength(header.PayloadLength))
            {
                continue;
            }

            var frameEnd = candidate + WalFrame.HeaderLength + header.PayloadLength;
            if (frameEnd <= fileLength
                && Crc32.Compute(ReadBytes(candidate + WalFrame.HeaderLength, header.PayloadLength)) == header.PayloadChecksum)
            {
                return true;
            }
        }

        return false;
    }

    private byte[] ReadBytes(long offset, int count)
    {
        var buffer = new byte[count];
        stream.Position = offset;
        stream.ReadExactly(buffer);
        return buffer;
    }

    private bool IsZeroFilledFrom(long offset, long fileLength)
    {
        var buffer = new byte[ZeroScanChunkLength];
        stream.Position = offset;

        var remaining = fileLength - offset;
        while (remaining > 0)
        {
            var count = (int)Math.Min(buffer.Length, remaining);
            stream.ReadExactly(buffer, 0, count);
            if (buffer.AsSpan(0, count).ContainsAnyExcept((byte)0))
            {
                return false;
            }

            remaining -= count;
        }

        return true;
    }

    /// <summary>
    /// The caller reports the original write failure. When the file cannot be cut back, the flag makes later
    /// appends refuse instead of writing after a torn frame.
    /// </summary>
    private bool TryRestoreLength(long length)
    {
        try
        {
            stream.SetLength(length);
            stream.Flush(true);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private void ThrowIfBroken()
    {
        if (isBroken)
        {
            throw new NumoSqlException(
                "the write-ahead log is in an unknown state after a failed write; close and reopen the database");
        }
    }

    private sealed record FrameRead(byte[] Payload, long EndOffset);
}
