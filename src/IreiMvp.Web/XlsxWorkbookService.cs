using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace IreiMvp.Web;

public sealed class XlsxWorkbookService
{
    private static readonly XNamespace Spreadsheet =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace OfficeRelationships =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelationships =
        "http://schemas.openxmlformats.org/package/2006/relationships";

    public WorkbookSnapshot Read(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var sharedStrings = ReadSharedStrings(archive);
        var sheetPaths = ReadSheetPaths(archive);
        var sheets = new Dictionary<string, SheetSnapshot>(StringComparer.OrdinalIgnoreCase);

        foreach (var (sheetName, entryPath) in sheetPaths)
        {
            var entry = archive.GetEntry(entryPath);
            if (entry is null)
            {
                continue;
            }

            using var stream = entry.Open();
            var document = XDocument.Load(stream);
            var cells = new Dictionary<string, CellSnapshot>(StringComparer.OrdinalIgnoreCase);
            var maxRow = 0;
            var maxColumn = 0;

            foreach (var cell in document.Descendants(Spreadsheet + "c"))
            {
                var reference = (string?)cell.Attribute("r");
                if (string.IsNullOrWhiteSpace(reference))
                {
                    continue;
                }

                var (row, column) = XlsxAddress.Parse(reference);
                maxRow = Math.Max(maxRow, row);
                maxColumn = Math.Max(maxColumn, column);

                var cellType = (string?)cell.Attribute("t");
                var formula = cell.Element(Spreadsheet + "f")?.Value;
                var value = ReadCellValue(cell, cellType, sharedStrings);

                cells[reference] = new CellSnapshot
                {
                    Value = value,
                    Formula = formula
                };
            }

            sheets[sheetName] = new SheetSnapshot
            {
                Name = sheetName,
                Cells = cells,
                MaxRow = maxRow,
                MaxColumn = maxColumn
            };
        }

        return new WorkbookSnapshot
        {
            Sha256 = ComputeSha256(path),
            Sheets = sheets
        };
    }

    public void CopyAndPatch(
        string templatePath,
        string outputPath,
        IReadOnlyCollection<CellPatch> patches)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.Copy(templatePath, outputPath, overwrite: true);

        using var archive = ZipFile.Open(outputPath, ZipArchiveMode.Update);
        var sheetPaths = ReadSheetPaths(archive);

        foreach (var group in patches.GroupBy(p => p.Sheet, StringComparer.OrdinalIgnoreCase))
        {
            if (!sheetPaths.TryGetValue(group.Key, out var entryPath))
            {
                continue;
            }

            var entry = archive.GetEntry(entryPath);
            if (entry is null)
            {
                continue;
            }

            XDocument document;
            using (var stream = entry.Open())
            {
                document = XDocument.Load(stream);
            }

            var styleByColumn = BuildStyleMap(document);

            foreach (var patch in group)
            {
                ApplyPatch(document, patch, styleByColumn);
            }

            UpdateDimension(document);
            ReplaceEntry(archive, entryPath, document);
        }

        SetWorkbookRecalculation(archive);
        RemoveCalculationChain(archive);
    }

    private static IReadOnlyList<string> ReadSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
        {
            return Array.Empty<string>();
        }

        using var stream = entry.Open();
        var document = XDocument.Load(stream);

        return document
            .Descendants(Spreadsheet + "si")
            .Select(item => string.Concat(item.Descendants(Spreadsheet + "t").Select(t => t.Value)))
            .ToList();
    }

    private static Dictionary<string, string> ReadSheetPaths(ZipArchive archive)
    {
        var workbookEntry = archive.GetEntry("xl/workbook.xml")
            ?? throw new InvalidDataException("The workbook package does not contain xl/workbook.xml.");
        var relationshipsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels")
            ?? throw new InvalidDataException("The workbook package does not contain workbook relationships.");

        XDocument workbook;
        XDocument relationships;

        using (var stream = workbookEntry.Open())
        {
            workbook = XDocument.Load(stream);
        }

        using (var stream = relationshipsEntry.Open())
        {
            relationships = XDocument.Load(stream);
        }

        var relationshipMap = relationships
            .Descendants(PackageRelationships + "Relationship")
            .Where(r => r.Attribute("Id") is not null && r.Attribute("Target") is not null)
            .ToDictionary(
                r => (string)r.Attribute("Id")!,
                r => NormalizeWorksheetPath((string)r.Attribute("Target")!),
                StringComparer.OrdinalIgnoreCase);

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var sheet in workbook.Descendants(Spreadsheet + "sheet"))
        {
            var name = (string?)sheet.Attribute("name");
            var relationshipId = (string?)sheet.Attribute(OfficeRelationships + "id");

            if (string.IsNullOrWhiteSpace(name) ||
                string.IsNullOrWhiteSpace(relationshipId) ||
                !relationshipMap.TryGetValue(relationshipId, out var path))
            {
                continue;
            }

            result[name] = path;
        }

        return result;
    }

    private static string NormalizeWorksheetPath(string target)
    {
        var normalized = target.Replace('\\', '/');

        if (normalized.StartsWith('/'))
        {
            return normalized.TrimStart('/');
        }

        if (normalized.StartsWith("xl/", StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        return "xl/" + normalized.TrimStart('/');
    }

    private static object? ReadCellValue(
        XElement cell,
        string? cellType,
        IReadOnlyList<string> sharedStrings)
    {
        if (string.Equals(cellType, "inlineStr", StringComparison.OrdinalIgnoreCase))
        {
            return string.Concat(
                cell.Element(Spreadsheet + "is")?.Descendants(Spreadsheet + "t")
                    .Select(t => t.Value) ?? Enumerable.Empty<string>());
        }

        var raw = cell.Element(Spreadsheet + "v")?.Value;
        if (raw is null)
        {
            return null;
        }

        if (string.Equals(cellType, "s", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) &&
            index >= 0 && index < sharedStrings.Count)
        {
            return sharedStrings[index];
        }

        if (string.Equals(cellType, "b", StringComparison.OrdinalIgnoreCase))
        {
            return raw == "1";
        }

        if (string.Equals(cellType, "str", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(cellType, "e", StringComparison.OrdinalIgnoreCase))
        {
            return raw;
        }

        if (decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var number))
        {
            return number;
        }

        return raw;
    }

    private static Dictionary<int, string> BuildStyleMap(XDocument document)
    {
        var map = new Dictionary<int, string>();

        foreach (var cell in document.Descendants(Spreadsheet + "c"))
        {
            var reference = (string?)cell.Attribute("r");
            var style = (string?)cell.Attribute("s");

            if (reference is null || style is null)
            {
                continue;
            }

            var (_, column) = XlsxAddress.Parse(reference);
            map.TryAdd(column, style);
        }

        return map;
    }

    private static void ApplyPatch(
        XDocument document,
        CellPatch patch,
        IReadOnlyDictionary<int, string> styleByColumn)
    {
        var root = document.Root
            ?? throw new InvalidDataException("Worksheet XML has no root element.");
        var sheetData = root.Element(Spreadsheet + "sheetData");

        if (sheetData is null)
        {
            sheetData = new XElement(Spreadsheet + "sheetData");
            root.Add(sheetData);
        }

        var (rowNumber, columnNumber) = XlsxAddress.Parse(patch.Cell);
        var row = GetOrCreateRow(sheetData, rowNumber);
        var cell = GetOrCreateCell(row, patch.Cell, columnNumber, styleByColumn);

        cell.Elements()
            .Where(element =>
                element.Name == Spreadsheet + "f" ||
                element.Name == Spreadsheet + "v" ||
                element.Name == Spreadsheet + "is")
            .Remove();
        cell.Attribute("t")?.Remove();

        if (patch.Clear)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(patch.Formula))
        {
            cell.Add(new XElement(Spreadsheet + "f", patch.Formula));
            if (patch.CachedValue is not null)
            {
                WriteCachedValue(cell, patch.CachedValue);
            }

            return;
        }

        if (patch.Value is not null)
        {
            WriteDirectValue(cell, patch.Value);
        }
    }

    private static XElement GetOrCreateRow(XElement sheetData, int rowNumber)
    {
        var row = sheetData
            .Elements(Spreadsheet + "row")
            .FirstOrDefault(r => (int?)r.Attribute("r") == rowNumber);

        if (row is not null)
        {
            return row;
        }

        row = new XElement(Spreadsheet + "row", new XAttribute("r", rowNumber));
        var nextRow = sheetData
            .Elements(Spreadsheet + "row")
            .FirstOrDefault(r => ((int?)r.Attribute("r") ?? int.MaxValue) > rowNumber);

        if (nextRow is null)
        {
            sheetData.Add(row);
        }
        else
        {
            nextRow.AddBeforeSelf(row);
        }

        return row;
    }

    private static XElement GetOrCreateCell(
        XElement row,
        string reference,
        int columnNumber,
        IReadOnlyDictionary<int, string> styleByColumn)
    {
        var cell = row
            .Elements(Spreadsheet + "c")
            .FirstOrDefault(c =>
                string.Equals((string?)c.Attribute("r"), reference, StringComparison.OrdinalIgnoreCase));

        if (cell is not null)
        {
            return cell;
        }

        cell = new XElement(Spreadsheet + "c", new XAttribute("r", reference));
        if (styleByColumn.TryGetValue(columnNumber, out var style))
        {
            cell.SetAttributeValue("s", style);
        }

        var nextCell = row
            .Elements(Spreadsheet + "c")
            .FirstOrDefault(c =>
            {
                var cellReference = (string?)c.Attribute("r");
                if (cellReference is null)
                {
                    return false;
                }

                var (_, existingColumn) = XlsxAddress.Parse(cellReference);
                return existingColumn > columnNumber;
            });

        if (nextCell is null)
        {
            row.Add(cell);
        }
        else
        {
            nextCell.AddBeforeSelf(cell);
        }

        return cell;
    }

    private static void WriteDirectValue(XElement cell, object value)
    {
        value = UnwrapJsonElement(value);

        switch (value)
        {
            case null:
                return;
            case bool boolean:
                cell.SetAttributeValue("t", "b");
                cell.Add(new XElement(Spreadsheet + "v", boolean ? "1" : "0"));
                return;
            case byte or sbyte or short or ushort or int or uint or long or ulong or
                 float or double or decimal:
                cell.Add(new XElement(
                    Spreadsheet + "v",
                    Convert.ToString(value, CultureInfo.InvariantCulture)));
                return;
            default:
                var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
                cell.SetAttributeValue("t", "inlineStr");
                var textElement = new XElement(Spreadsheet + "t", text);
                if (text.StartsWith(' ') || text.EndsWith(' '))
                {
                    textElement.SetAttributeValue(XNamespace.Xml + "space", "preserve");
                }

                cell.Add(new XElement(Spreadsheet + "is", textElement));
                return;
        }
    }

    private static void WriteCachedValue(XElement cell, object value)
    {
        value = UnwrapJsonElement(value);

        switch (value)
        {
            case null:
                return;
            case bool boolean:
                cell.SetAttributeValue("t", "b");
                cell.Add(new XElement(Spreadsheet + "v", boolean ? "1" : "0"));
                return;
            case byte or sbyte or short or ushort or int or uint or long or ulong or
                 float or double or decimal:
                cell.Add(new XElement(
                    Spreadsheet + "v",
                    Convert.ToString(value, CultureInfo.InvariantCulture)));
                return;
            default:
                cell.SetAttributeValue("t", "str");
                cell.Add(new XElement(
                    Spreadsheet + "v",
                    Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""));
                return;
        }
    }

    private static object? UnwrapJsonElement(object value)
    {
        if (value is not JsonElement element)
        {
            return value;
        }

        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out var integer) => integer,
            JsonValueKind.Number when element.TryGetDecimal(out var number) => number,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.ToString()
        };
    }

    private static void UpdateDimension(XDocument document)
    {
        var root = document.Root;
        if (root is null)
        {
            return;
        }

        var references = document
            .Descendants(Spreadsheet + "c")
            .Select(c => (string?)c.Attribute("r"))
            .Where(reference => !string.IsNullOrWhiteSpace(reference))
            .Select(reference => XlsxAddress.Parse(reference!))
            .ToList();

        if (references.Count == 0)
        {
            return;
        }

        var maxRow = references.Max(item => item.Row);
        var maxColumn = references.Max(item => item.Column);
        var dimensionReference = $"A1:{XlsxAddress.ToCellReference(maxRow, maxColumn)}";

        var dimension = root.Element(Spreadsheet + "dimension");
        if (dimension is null)
        {
            dimension = new XElement(
                Spreadsheet + "dimension",
                new XAttribute("ref", dimensionReference));
            root.AddFirst(dimension);
        }
        else
        {
            dimension.SetAttributeValue("ref", dimensionReference);
        }
    }

    private static void SetWorkbookRecalculation(ZipArchive archive)
    {
        const string entryPath = "xl/workbook.xml";
        var entry = archive.GetEntry(entryPath);
        if (entry is null)
        {
            return;
        }

        XDocument document;
        using (var stream = entry.Open())
        {
            document = XDocument.Load(stream);
        }

        var root = document.Root;
        if (root is null)
        {
            return;
        }

        var calculationProperties = root.Element(Spreadsheet + "calcPr");
        if (calculationProperties is null)
        {
            calculationProperties = new XElement(Spreadsheet + "calcPr");
            root.Add(calculationProperties);
        }

        calculationProperties.SetAttributeValue("calcMode", "auto");
        calculationProperties.SetAttributeValue("fullCalcOnLoad", "1");
        calculationProperties.SetAttributeValue("forceFullCalc", "1");
        calculationProperties.SetAttributeValue("calcId", "0");

        ReplaceEntry(archive, entryPath, document);
    }


    private static void RemoveCalculationChain(ZipArchive archive)
    {
        archive.GetEntry("xl/calcChain.xml")?.Delete();

        const string relationshipsPath = "xl/_rels/workbook.xml.rels";
        var relationshipsEntry = archive.GetEntry(relationshipsPath);
        if (relationshipsEntry is not null)
        {
            XDocument relationships;
            using (var stream = relationshipsEntry.Open())
            {
                relationships = XDocument.Load(stream);
            }

            relationships
                .Descendants(PackageRelationships + "Relationship")
                .Where(element =>
                    ((string?)element.Attribute("Type"))?.EndsWith(
                        "/calcChain",
                        StringComparison.OrdinalIgnoreCase) == true)
                .Remove();

            ReplaceEntry(archive, relationshipsPath, relationships);
        }

        const string contentTypesPath = "[Content_Types].xml";
        var contentTypesEntry = archive.GetEntry(contentTypesPath);
        if (contentTypesEntry is not null)
        {
            XDocument contentTypes;
            using (var stream = contentTypesEntry.Open())
            {
                contentTypes = XDocument.Load(stream);
            }

            contentTypes
                .Descendants()
                .Where(element =>
                    string.Equals(
                        (string?)element.Attribute("PartName"),
                        "/xl/calcChain.xml",
                        StringComparison.OrdinalIgnoreCase))
                .Remove();

            ReplaceEntry(archive, contentTypesPath, contentTypes);
        }
    }

    private static void ReplaceEntry(
        ZipArchive archive,
        string entryPath,
        XDocument document)
    {
        archive.GetEntry(entryPath)?.Delete();
        var newEntry = archive.CreateEntry(entryPath, CompressionLevel.Optimal);

        using var stream = newEntry.Open();
        using var writer = new StreamWriter(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        document.Save(writer, SaveOptions.DisableFormatting);
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
