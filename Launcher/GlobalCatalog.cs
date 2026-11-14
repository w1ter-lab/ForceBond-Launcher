using System.Text;
using System.Text.RegularExpressions;

namespace ForceBond.Launcher;

internal static class GlobalCatalog
{
    private sealed record Row(string Label, string[] Cells);
    private sealed record Table(string[] Headers, List<Row> Rows);

    // Append our Number globals without replacing globals installed by other mods.
    public static byte[] Merge(byte[] baseline, byte[]? installed)
    {
        if (installed is null) return baseline;
        Table source = Read(baseline), target = Read(installed);
        int sourceName = Column(source, "Name"), sourceType = Column(source, "Type");
        int targetName = Column(target, "Name"), targetType = Column(target, "Type");
        var required = source.Rows.Where(row => row.Cells[sourceName].StartsWith("ODMP_", StringComparison.Ordinal) || row.Cells[sourceName].StartsWith("ODP_", StringComparison.Ordinal))
            .Select(row => row.Cells[sourceName]).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int removed = target.Rows.RemoveAll(row =>
            (row.Cells[targetName].StartsWith("ODMP_", StringComparison.OrdinalIgnoreCase) || row.Cells[targetName].StartsWith("ODP_", StringComparison.OrdinalIgnoreCase)) &&
            !required.Contains(row.Cells[targetName]));
        var names = target.Rows.ToDictionary(row => row.Cells[targetName], StringComparer.OrdinalIgnoreCase);
        int nextLabel = target.Rows.Select(row => int.TryParse(row.Label, out int n) ? n : -1).DefaultIfEmpty(-1).Max() + 1;
        bool changed = removed > 0;
        foreach (Row row in source.Rows.Where(row => (row.Cells[sourceName].StartsWith("ODMP_", StringComparison.Ordinal) || row.Cells[sourceName].StartsWith("ODP_", StringComparison.Ordinal))))
        {
            string name = row.Cells[sourceName];
            if (!row.Cells[sourceType].Equals("String", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Force Bond global has an invalid type: {name}");
            if (names.TryGetValue(name, out Row? existing))
            {
                if (!existing.Cells[targetType].Equals("String", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Global catalog conflict: {name} must have type String.");
                continue;
            }
            var cells = Enumerable.Repeat(string.Empty, target.Headers.Length).ToArray();
            cells[targetName] = name;
            cells[targetType] = "String";
            var added = new Row((nextLabel++).ToString(System.Globalization.CultureInfo.InvariantCulture), cells);
            target.Rows.Add(added);
            names.Add(name, added);
            changed = true;
        }
        int CountType(string kind) => target.Rows.Count(row => row.Cells[targetType].Equals(kind, StringComparison.OrdinalIgnoreCase));
        if (CountType("Number") > 1000 || CountType("Boolean") > 1000 || CountType("Location") > 100 || CountType("String") > 5)
            throw new InvalidDataException("Global catalog exceeds the engine's per-type capacity (Number 1000, Boolean 1000, Location 100, String 5). No files were installed.");
        if (target.Rows.Count > 2365) throw new InvalidDataException("The merged global catalog exceeds the supported engine capacity (2365). No files were installed.");
        if (!changed) return installed;
        return WriteBinary(target);
    }

    private static int Column(Table table, string name)
    {
        int index = Array.FindIndex(table.Headers, column => column.Equals(name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 ? index : throw new InvalidDataException($"globalcat.2da has no {name} column.");
    }

    private static byte[] WriteBinary(Table table)
    {
        using var values = new MemoryStream();
        var strings = new Dictionary<string, ushort>(StringComparer.Ordinal);
        var offsets = new List<ushort>();
        foreach (Row row in table.Rows)
        foreach (string cell in row.Cells)
        {
            if (!strings.TryGetValue(cell, out ushort offset))
            {
                byte[] text = Encoding.Latin1.GetBytes(cell);
                if (values.Length + text.Length + 1 > ushort.MaxValue)
                    throw new InvalidDataException("Merged globalcat.2da exceeds the game's 16-bit string table limit.");
                offset = (ushort)values.Length;
                strings.Add(cell, offset);
                values.Write(text);
                values.WriteByte(0);
            }
            offsets.Add(offset);
        }
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output, Encoding.Latin1, true);
        writer.Write(Encoding.ASCII.GetBytes("2DA V2.b\n"));
        writer.Write(Encoding.Latin1.GetBytes(string.Join('\t', table.Headers) + "\t"));
        writer.Write((byte)0);
        writer.Write((uint)table.Rows.Count);
        foreach (Row row in table.Rows) writer.Write(Encoding.Latin1.GetBytes(row.Label + "\t"));
        foreach (ushort offset in offsets) writer.Write(offset);
        writer.Write((ushort)values.Length);
        writer.Write(values.ToArray());
        return output.ToArray();
    }

    private static Table Read(byte[] data)
    {
        if (data.Length < 9) throw new InvalidDataException("Truncated globalcat.2da.");
        string signature = Encoding.ASCII.GetString(data, 0, 8);
        if (signature == "2DA V2.0")
        {
            string[] lines = Encoding.Latin1.GetString(data).Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).ToArray();
            int headerLine = 1;
            string defaultValue = "";
            if (lines.Length > 1 && lines[1].StartsWith("DEFAULT:", StringComparison.OrdinalIgnoreCase))
            {
                defaultValue = lines[1][8..].Trim();
                headerLine++;
            }
            if (defaultValue == "****") defaultValue = "";
            string[] headers = Tokens(lines[headerLine]);
            var rows = new List<Row>();
            foreach (string line in lines.Skip(headerLine + 1))
            {
                string[] tokens = Tokens(line);
                if (tokens.Length > headers.Length + 1) throw new InvalidDataException("Invalid globalcat.2da row.");
                string[] cells = Enumerable.Repeat(defaultValue, headers.Length).ToArray();
                Array.Copy(tokens, 1, cells, 0, tokens.Length - 1);
                rows.Add(new Row(tokens[0], cells));
            }
            return new Table(headers, rows);
        }
        if (signature != "2DA V2.b") throw new InvalidDataException("Unsupported globalcat.2da format.");
        using var stream = new MemoryStream(data, false);
        using var reader = new BinaryReader(stream, Encoding.Latin1);
        stream.Position = 9;
        string columns = ReadUntil(reader, 0);
        string[] binaryHeaders = columns.Split('\t', StringSplitOptions.RemoveEmptyEntries);
        uint count = reader.ReadUInt32();
        if (count > 65535 || binaryHeaders.Length == 0) throw new InvalidDataException("Invalid globalcat.2da dimensions.");
        string[] labels = Enumerable.Range(0, (int)count).Select(_ => ReadUntil(reader, 9)).ToArray();
        ushort[] offsets = Enumerable.Range(0, checked((int)count * binaryHeaders.Length)).Select(_ => reader.ReadUInt16()).ToArray();
        ushort dataSize = reader.ReadUInt16();
        long dataStart = stream.Position;
        if (dataStart + dataSize > stream.Length) throw new InvalidDataException("Truncated globalcat.2da cell data.");
        var binaryRows = new List<Row>();
        for (int row = 0; row < count; ++row)
        {
            string[] cells = new string[binaryHeaders.Length];
            for (int column = 0; column < cells.Length; ++column)
            {
                ushort offset = offsets[row * cells.Length + column];
                if (offset >= dataSize) throw new InvalidDataException("Invalid globalcat.2da cell offset.");
                stream.Position = dataStart + offset;
                cells[column] = ReadUntil(reader, 0);
            }
            binaryRows.Add(new Row(labels[row], cells));
        }
        return new Table(binaryHeaders, binaryRows);
    }

    private static string[] Tokens(string line) => Regex.Matches(line, "\"([^\"]*)\"|(\\S+)")
        .Select(match => match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value)
        .Select(value => value == "****" ? "" : value).ToArray();

    private static string ReadUntil(BinaryReader reader, byte terminator)
    {
        var bytes = new List<byte>();
        byte value;
        while ((value = reader.ReadByte()) != terminator) bytes.Add(value);
        return Encoding.Latin1.GetString(bytes.ToArray());
    }
}
