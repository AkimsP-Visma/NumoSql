using System.Buffers.Binary;
using System.Text;

namespace NumoSql.Storage;

/// <summary>
/// On-disk layout of one log record: payload length, payload CRC-32, header CRC-32 covering the first eight bytes,
/// then the payload. The header checksum lets a damaged length be told apart from an incomplete final write.
/// </summary>
internal static class WalFrame
{
    public const int HeaderLength = 12;
    public const int MinimumPayloadLength = sizeof(long) + sizeof(int);
    public const int MaximumPayloadLength = 512 * 1024 * 1024;

    /// <summary>Throws on a lone surrogate. The default encoder would store U+FFFD instead, and replay would rebuild different data.</summary>
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private const int LengthOffset = 0;
    private const int PayloadChecksumOffset = 4;
    private const int HeaderChecksumOffset = 8;
    private const int CheckedHeaderLength = 8;

    public static byte[] Encode(WalRecord record)
    {
        var payload = EncodePayload(record);
        if (payload.Length > MaximumPayloadLength)
        {
            throw new NumoSqlException("transaction is too large to commit at once, split it into smaller transactions");
        }

        var frame = new byte[HeaderLength + payload.Length];
        var span = frame.AsSpan();
        BinaryPrimitives.WriteInt32LittleEndian(span[LengthOffset..], payload.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(span[PayloadChecksumOffset..], Crc32.Compute(payload));
        BinaryPrimitives.WriteUInt32LittleEndian(
            span[HeaderChecksumOffset..],
            Crc32.Compute(span[..CheckedHeaderLength]));
        payload.CopyTo(frame, HeaderLength);
        return frame;
    }

    public static FrameHeader ParseHeader(ReadOnlySpan<byte> header)
    {
        var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(header[LengthOffset..]);
        var payloadChecksum = BinaryPrimitives.ReadUInt32LittleEndian(header[PayloadChecksumOffset..]);
        var storedHeaderChecksum = BinaryPrimitives.ReadUInt32LittleEndian(header[HeaderChecksumOffset..]);
        var isIntact = Crc32.Compute(header[..CheckedHeaderLength]) == storedHeaderChecksum;
        return new FrameHeader(payloadLength, payloadChecksum, isIntact);
    }

    public static bool IsValidPayloadLength(int payloadLength) =>
        payloadLength >= MinimumPayloadLength && payloadLength <= MaximumPayloadLength;

    public static WalRecord Decode(byte[] payload)
    {
        using var stream = new MemoryStream(payload, writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        var logSequenceNumber = reader.ReadInt64();
        var statementCount = reader.ReadInt32();
        if (statementCount < 0)
        {
            throw new DatabaseCorruptException("write-ahead log record declares a negative statement count");
        }

        var statements = new List<string>(statementCount);
        for (var index = 0; index < statementCount; index++)
        {
            statements.Add(reader.ReadString());
        }

        return new WalRecord(logSequenceNumber, statements);
    }

    private static byte[] EncodePayload(WalRecord record)
    {
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer, StrictUtf8, leaveOpen: true);

        writer.Write(record.LogSequenceNumber);
        writer.Write(record.Statements.Count);
        foreach (var statement in record.Statements)
        {
            writer.Write(statement);
        }

        writer.Flush();
        return buffer.ToArray();
    }
}

/// <param name="IsIntact">False when the header checksum does not match, so the length cannot be trusted.</param>
internal readonly record struct FrameHeader(int PayloadLength, uint PayloadChecksum, bool IsIntact);
