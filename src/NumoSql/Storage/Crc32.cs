namespace NumoSql.Storage;

/// <summary>CRC-32 (IEEE 802.3), the checksum used by zip and PNG. Standard value for "123456789" is 0xCBF43926.</summary>
internal static class Crc32
{
    public const uint Initial = 0xFFFFFFFFu;

    private const uint ReversedPolynomial = 0xEDB88320u;
    private const uint ByteMask = 0xFFu;
    private const int TableSize = 256;
    private const int BitsPerByte = 8;

    private static readonly uint[] Table = BuildTable();

    /// <summary>Folds bytes into a running state. Start from <see cref="Initial"/> and finish with <see cref="Finish"/>.</summary>
    public static uint Update(uint state, ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes)
        {
            state = Table[(state ^ value) & ByteMask] ^ (state >> BitsPerByte);
        }

        return state;
    }

    public static uint Finish(uint state) => ~state;

    public static uint Compute(ReadOnlySpan<byte> bytes) => Finish(Update(Initial, bytes));

    private static uint[] BuildTable()
    {
        var table = new uint[TableSize];
        for (var index = 0; index < TableSize; index++)
        {
            var value = (uint)index;
            for (var bit = 0; bit < BitsPerByte; bit++)
            {
                value = (value & 1) != 0 ? ReversedPolynomial ^ (value >> 1) : value >> 1;
            }

            table[index] = value;
        }

        return table;
    }
}
