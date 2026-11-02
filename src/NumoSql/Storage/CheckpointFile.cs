using System.Buffers.Binary;
using System.Text;
using NumoSql.Schema;
using NumoSql.Types;

namespace NumoSql.Storage;

/// <summary>
/// Full snapshot of the committed tables in binary form, with a trailing CRC-32 over the whole body. The file is
/// written under a temporary name and renamed into place, so a crash leaves either the old checkpoint or the new one.
/// </summary>
internal static class CheckpointFile
{
    private const string TemporarySuffix = ".tmp";
    private const int FormatVersion = 1;
    private const int NoPrimaryKey = -1;
    private const long NoCheckpoint = 0;
    private const int ChecksumLength = sizeof(uint);
    private const int ChecksumChunkLength = 1 << 20;
    private const int FixedHeaderLength = sizeof(int) + sizeof(long) + sizeof(int);

    private static readonly byte[] Magic = "NUMOCKPT"u8.ToArray();

    /// <summary>Throws on a lone surrogate, so a checkpoint never stores text that would be read back differently.</summary>
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private enum ValueTag : byte
    {
        Null,
        Integer,
        Real,
        Text,
        Boolean,
    }

    public static void Write(string path, Catalog catalog, long logSequenceNumber)
    {
        var temporaryPath = path + TemporarySuffix;
        using (var file = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            using var checksumming = new ChecksummingStream(file);
            WriteBody(checksumming, catalog, logSequenceNumber);
            WriteChecksumTrailer(file, checksumming.Checksum);
            file.Flush(true);
        }

        File.Move(temporaryPath, path, overwrite: true);
    }

    /// <summary>Loads the checkpoint into the catalog and returns the sequence number it covers, or 0 when none exists.</summary>
    public static long Load(string path, Catalog catalog)
    {
        if (!File.Exists(path))
        {
            return NoCheckpoint;
        }

        VerifyChecksum(path);

        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(file, Encoding.UTF8, leaveOpen: true);

            ValidateHeader(reader, path);
            var logSequenceNumber = reader.ReadInt64();
            var tableCount = reader.ReadInt32();
            for (var index = 0; index < tableCount; index++)
            {
                LoadTable(reader, catalog);
            }

            return logSequenceNumber;
        }
        catch (EndOfStreamException endOfData)
        {
            throw new DatabaseCorruptException($"checkpoint {path} ends before its declared contents", endOfData);
        }
        catch (NumoSqlException invalidContent) when (invalidContent is not DatabaseCorruptException)
        {
            throw new DatabaseCorruptException($"checkpoint {path} holds invalid data: {invalidContent.Message}", invalidContent);
        }
    }

    /// <summary>Removes a temporary file left by a crash during Write. Such a file was never renamed into place.</summary>
    public static void DeleteIncomplete(string path)
    {
        var temporaryPath = path + TemporarySuffix;
        if (File.Exists(temporaryPath))
        {
            File.Delete(temporaryPath);
        }
    }

    private static void WriteBody(Stream destination, Catalog catalog, long logSequenceNumber)
    {
        using var writer = new BinaryWriter(destination, StrictUtf8, leaveOpen: true);
        writer.Write(Magic);
        writer.Write(FormatVersion);
        writer.Write(logSequenceNumber);

        var tables = catalog.Tables.OrderBy(table => table.Definition.Name, StringComparer.Ordinal).ToList();
        writer.Write(tables.Count);
        foreach (var table in tables)
        {
            WriteTable(writer, table);
        }

        writer.Flush();
    }

    private static void WriteChecksumTrailer(FileStream file, uint checksum)
    {
        Span<byte> trailer = stackalloc byte[ChecksumLength];
        BinaryPrimitives.WriteUInt32LittleEndian(trailer, checksum);
        file.Write(trailer);
    }

    private static void WriteTable(BinaryWriter writer, Table table)
    {
        var definition = table.Definition;
        writer.Write(definition.Name);
        writer.Write(definition.Columns.Count);
        foreach (var column in definition.Columns)
        {
            writer.Write(column.Name);
            writer.Write((byte)column.Type);
            writer.Write(column.IsNotNull);
        }

        writer.Write(definition.PrimaryKeyIndex ?? NoPrimaryKey);
        writer.Write((long)table.Rows.Count);
        foreach (var row in table.Rows)
        {
            foreach (var value in row)
            {
                WriteValue(writer, value);
            }
        }
    }

    private static void WriteValue(BinaryWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.Write((byte)ValueTag.Null);
                break;
            case long integer:
                writer.Write((byte)ValueTag.Integer);
                writer.Write(integer);
                break;
            case double real:
                writer.Write((byte)ValueTag.Real);
                writer.Write(real);
                break;
            case string text:
                writer.Write((byte)ValueTag.Text);
                writer.Write(text);
                break;
            case bool boolean:
                writer.Write((byte)ValueTag.Boolean);
                writer.Write(boolean);
                break;
            default:
                throw new InvalidOperationException($"value of type {value.GetType().Name} cannot be checkpointed");
        }
    }

    private static void VerifyChecksum(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length < Magic.Length + FixedHeaderLength + ChecksumLength)
        {
            throw new DatabaseCorruptException($"checkpoint {path} is too short to be valid");
        }

        var buffer = new byte[ChecksumChunkLength];
        var state = Crc32.Initial;
        var remaining = file.Length - ChecksumLength;
        while (remaining > 0)
        {
            var count = (int)Math.Min(buffer.Length, remaining);
            file.ReadExactly(buffer, 0, count);
            state = Crc32.Update(state, buffer.AsSpan(0, count));
            remaining -= count;
        }

        Span<byte> trailer = stackalloc byte[ChecksumLength];
        file.ReadExactly(trailer);
        if (Crc32.Finish(state) != BinaryPrimitives.ReadUInt32LittleEndian(trailer))
        {
            throw new DatabaseCorruptException($"checkpoint {path} fails its checksum");
        }
    }

    private static void ValidateHeader(BinaryReader reader, string path)
    {
        var magic = reader.ReadBytes(Magic.Length);
        if (!magic.AsSpan().SequenceEqual(Magic))
        {
            throw new DatabaseCorruptException($"{path} is not a NUMOSQL checkpoint");
        }

        var version = reader.ReadInt32();
        if (version != FormatVersion)
        {
            throw new DatabaseCorruptException($"checkpoint format version {version} is not supported");
        }
    }

    private static void LoadTable(BinaryReader reader, Catalog catalog)
    {
        var tableName = reader.ReadString();
        var columnCount = reader.ReadInt32();
        var columns = new List<ColumnDefinition>(columnCount);
        for (var index = 0; index < columnCount; index++)
        {
            var columnName = reader.ReadString();
            var columnType = ReadSqlType(reader);
            var isNotNull = reader.ReadBoolean();
            columns.Add(new ColumnDefinition(columnName, columnType, isNotNull));
        }

        var primaryKeyIndex = reader.ReadInt32();
        var definition = new TableDefinition(
            tableName,
            columns,
            primaryKeyIndex == NoPrimaryKey ? (int?)null : primaryKeyIndex);

        var rowCount = reader.ReadInt64();
        var rows = new List<object?[]>();
        for (var index = 0L; index < rowCount; index++)
        {
            rows.Add(ReadRow(reader, columns));
        }

        if (catalog.Find(tableName) is not null)
        {
            throw new DatabaseCorruptException($"table \"{tableName}\" appears more than once in the checkpoint");
        }

        var table = new Table(definition);
        _ = table.AppendRows(rows);
        catalog.Add(table);
    }

    private static object?[] ReadRow(BinaryReader reader, IReadOnlyList<ColumnDefinition> columns)
    {
        var row = new object?[columns.Count];
        for (var index = 0; index < columns.Count; index++)
        {
            row[index] = ValueConversion.ToColumnValue(ReadValue(reader), columns[index]);
        }

        return row;
    }

    private static object? ReadValue(BinaryReader reader) => (ValueTag)reader.ReadByte() switch
    {
        ValueTag.Null => null,
        ValueTag.Integer => reader.ReadInt64(),
        ValueTag.Real => reader.ReadDouble(),
        ValueTag.Text => reader.ReadString(),
        ValueTag.Boolean => reader.ReadBoolean(),
        _ => throw new DatabaseCorruptException("checkpoint contains an unknown value tag"),
    };

    private static SqlType ReadSqlType(BinaryReader reader)
    {
        var code = reader.ReadByte();
        if (!Enum.IsDefined((SqlType)code))
        {
            throw new DatabaseCorruptException($"checkpoint contains an unknown column type {code}");
        }

        return (SqlType)code;
    }
}
