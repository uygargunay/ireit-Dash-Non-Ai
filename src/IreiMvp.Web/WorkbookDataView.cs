using System.Globalization;

namespace IreiMvp.Web;

internal sealed class WorkbookDataView
{
    private readonly WorkbookSnapshot _workbook;
    private readonly Dictionary<string, CellPatch> _patches;
    private readonly Dictionary<string, int> _patchedMaxRows;

    public WorkbookDataView(
        WorkbookSnapshot workbook,
        IEnumerable<CellPatch>? patches = null)
    {
        _workbook = workbook;
        _patches = (patches ?? [])
            .GroupBy(
                patch => Key(patch.Sheet, patch.Cell),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Last(),
                StringComparer.OrdinalIgnoreCase);

        _patchedMaxRows = _patches.Values
            .GroupBy(patch => patch.Sheet, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Max(patch => XlsxAddress.Parse(patch.Cell).Row),
                StringComparer.OrdinalIgnoreCase);
    }

    public string Control(string name)
    {
        if (!_workbook.Sheets.TryGetValue("MVP_CONTROL", out var sheet))
        {
            return "";
        }

        for (var row = 1; row <= MaxRow(sheet); row++)
        {
            if (string.Equals(
                    WorkbookValue.Text(Value(sheet.Name, row, 1)),
                    name,
                    StringComparison.OrdinalIgnoreCase))
            {
                return WorkbookValue.Text(Value(sheet.Name, row, 2));
            }
        }

        return "";
    }

    public IReadOnlyList<WorkbookRow> Rows(string sheetName)
    {
        if (!_workbook.Sheets.TryGetValue(sheetName, out var sheet))
        {
            return [];
        }

        var headerRow = FindHeaderRow(sheet);
        if (headerRow == 0)
        {
            return [];
        }

        var headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var column = 1; column <= sheet.MaxColumn; column++)
        {
            var header = WorkbookValue.Normalize(
                WorkbookValue.Text(Value(sheetName, headerRow, column)));
            if (!string.IsNullOrWhiteSpace(header))
            {
                headers.TryAdd(header, column);
            }
        }

        var rows = new List<WorkbookRow>();
        for (var row = headerRow + 1; row <= MaxRow(sheet); row++)
        {
            var item = new WorkbookRow(this, sheetName, row, headers);
            if (!item.IsBlank)
            {
                rows.Add(item);
            }
        }

        return rows;
    }

    public object? Value(string sheetName, int row, int column)
    {
        var cell = XlsxAddress.ToCellReference(row, column);
        if (_patches.TryGetValue(Key(sheetName, cell), out var patch))
        {
            if (patch.Clear)
            {
                return null;
            }

            return patch.CachedValue ?? patch.Value;
        }

        return _workbook.Sheets.TryGetValue(sheetName, out var sheet)
            ? sheet.GetValue(row, column)
            : null;
    }

    private int FindHeaderRow(SheetSnapshot sheet)
    {
        var bestRow = 0;
        var bestScore = 0;
        var maximumRow = Math.Min(MaxRow(sheet), 20);

        for (var row = 1; row <= maximumRow; row++)
        {
            var score = 0;
            for (var column = 1; column <= sheet.MaxColumn; column++)
            {
                var value = WorkbookValue.Text(Value(sheet.Name, row, column));
                if (!string.IsNullOrWhiteSpace(value))
                {
                    score++;
                }
            }

            if (score >= 2 && score > bestScore)
            {
                bestRow = row;
                bestScore = score;
            }
        }

        return bestRow;
    }

    private int MaxRow(SheetSnapshot sheet) => Math.Max(
        sheet.MaxRow,
        _patchedMaxRows.GetValueOrDefault(sheet.Name));

    private static string Key(string sheet, string cell) => $"{sheet}\u001f{cell}";
}

internal sealed class WorkbookRow
{
    private readonly WorkbookDataView _view;
    private readonly string _sheet;
    private readonly int _row;
    private readonly IReadOnlyDictionary<string, int> _headers;

    public WorkbookRow(
        WorkbookDataView view,
        string sheet,
        int row,
        IReadOnlyDictionary<string, int> headers)
    {
        _view = view;
        _sheet = sheet;
        _row = row;
        _headers = headers;
    }

    public int RowNumber => _row;

    public bool IsBlank => _headers.Values.All(column =>
        string.IsNullOrWhiteSpace(WorkbookValue.Text(_view.Value(_sheet, _row, column))));

    public object? Get(params string[] names)
    {
        foreach (var name in names)
        {
            if (_headers.TryGetValue(WorkbookValue.Normalize(name), out var column))
            {
                return _view.Value(_sheet, _row, column);
            }
        }

        return null;
    }

    public string Text(params string[] names) => WorkbookValue.Text(Get(names));

    public decimal? Decimal(params string[] names) => WorkbookValue.Decimal(Get(names));

    public int? Int(params string[] names) => WorkbookValue.Int(Get(names));

    public bool? Bool(params string[] names) => WorkbookValue.Bool(Get(names));

    public DateTimeOffset? Date(params string[] names) => WorkbookValue.Date(Get(names));
}

internal static class WorkbookValue
{
    public static string Text(object? value)
    {
        if (value is null)
        {
            return "";
        }

        return Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? "";
    }

    public static decimal? Decimal(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is decimal number)
        {
            return number;
        }

        if (value is int integer)
        {
            return integer;
        }

        var text = Text(value)
            .Replace("$", "", StringComparison.Ordinal)
            .Replace(",", "", StringComparison.Ordinal)
            .Replace("%", "", StringComparison.Ordinal)
            .Trim();

        if (text.StartsWith('(') && text.EndsWith(')'))
        {
            text = "-" + text[1..^1];
        }

        return decimal.TryParse(
            text,
            NumberStyles.Any,
            CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : null;
    }

    public static int? Int(object? value)
    {
        var number = Decimal(value);
        return number.HasValue
            ? Convert.ToInt32(decimal.Round(number.Value, MidpointRounding.AwayFromZero))
            : null;
    }

    public static bool? Bool(object? value)
    {
        if (value is bool boolean)
        {
            return boolean;
        }

        return Normalize(Text(value)) switch
        {
            "yes" or "true" or "included" or "1" => true,
            "no" or "false" or "excluded" or "0" => false,
            _ => null
        };
    }

    public static DateTimeOffset? Date(object? value)
    {
        if (value is DateTime dateTime)
        {
            return new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc));
        }

        if (value is DateTimeOffset dateTimeOffset)
        {
            return dateTimeOffset;
        }

        var number = Decimal(value);
        if (number is > 20_000 and < 80_000)
        {
            try
            {
                var oaDate = DateTime.FromOADate((double)number.Value);
                return new DateTimeOffset(DateTime.SpecifyKind(oaDate, DateTimeKind.Utc));
            }
            catch (ArgumentException)
            {
                // Fall through to text parsing.
            }
        }

        return DateTimeOffset.TryParse(
            Text(value),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? parsed
            : null;
    }

    public static string Normalize(string value) => new(
        value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
}
