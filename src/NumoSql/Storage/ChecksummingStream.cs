namespace NumoSql.Storage;

/// <summary>
/// Write-only pass-through that accumulates a CRC-32 over every byte written. It does not own the inner stream,
/// so the owner can still write the checksum trailer and flush the file.
/// </summary>
internal sealed class ChecksummingStream : Stream
{
    private readonly Stream inner;
    private uint state = Crc32.Initial;

    public ChecksummingStream(Stream inner)
    {
        this.inner = inner;
    }

    public uint Checksum => Crc32.Finish(state);

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush() => inner.Flush();

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        state = Crc32.Update(state, buffer);
        inner.Write(buffer);
    }
}
