using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace IreiMvp.Web;

public sealed class IntakeExtractor(XlsxWorkbookService xlsx)
{
    public static readonly string[] Codes = ["REVENUE", "OPEX", "OPEX_SOCIETY", "OPEX_BUILDINGS", "DEBT_PRINCIPAL", "DEBT_INTEREST", "DEBT_SERVICE", "RESERVE_CLOSING", "HOMES_TOTAL", "REV_SUBSIDY", "REV_RENT_EXISTING", "REV_RENT_NEW", "REV_OTHER"];
    public WorkbookSnapshot Read(string path, string name)
    {
        if (name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) return Csv(File.ReadAllText(path, new UTF8Encoding(false, true)));
        using (var z = ZipFile.OpenRead(path))
        {
            if (z.Entries.Sum(e => e.Length) > 100 * 1024 * 1024 || z.Entries.Count > 5000)
                throw new InvalidDataException("Expanded spreadsheet exceeds demonstration limits.");
            if (z.Entries.Any(e => e.FullName.Contains("vbaProject", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Macro-enabled sources require conversion; macros are never executed.");
        }
        return xlsx.Read(path);
    }
    public void Extract(WorkbookSnapshot w, IntakeSource source, List<IntakeCandidate> candidates)
    {
        source.Sheets.Clear();
        foreach (var s in w.Sheets.Values)
        {
            if (s.MaxRow > 100000 || s.MaxColumn > 1000 || s.Cells.Count > 200000)
                throw new InvalidDataException("Sheet exceeds demonstration row/column/cell limits; split or convert the source for review.");
            var present = s.Cells.Values.Where(c => c.Value is not null || c.Formula is not null).ToArray();
            source.Sheets.Add(new(s.Name, s.MaxRow, s.MaxColumn, present.Length, present.Count(c => c.Formula is not null),
                present.Count(c => c.Formula is not null && c.Value is null), present.Length == 0 ? "Empty" : "Inventoried"));
            // Find fiscal headers anywhere, not a fixed row or an organization-specific layout.
            var periods = s.Cells.Where(p => Regex.IsMatch(WorkbookValue.Text(p.Value.Value).Trim(), @"^\d{4,5}\s*[-–/]\s*\d{4}$"))
                .Select(p => (Position: XlsxAddress.Parse(p.Key), Text: WorkbookValue.Text(p.Value.Value))).ToArray();
            var propertyColumn = s.Cells.FirstOrDefault(p => WorkbookValue.Text(p.Value.Value).Trim().Equals("Property Name", StringComparison.OrdinalIgnoreCase));
            var propertyPos = propertyColumn.Key is null ? (Row: 0, Column: 0) : XlsxAddress.Parse(propertyColumn.Key);
            foreach (var (cell, item) in s.Cells)
            {
                var (row, col) = XlsxAddress.Parse(cell);
                var label = Enumerable.Range(1, Math.Max(0, col - 1)).Reverse().Select(c => WorkbookValue.Text(s.GetValue(row, c)))
                    .FirstOrDefault(v => v.Length > 0 && !decimal.TryParse(v, out _) && !v.StartsWith('=')) ?? "";
                var period = periods.Where(p => p.Position.Column == col && p.Position.Row < row).OrderByDescending(p => p.Position.Row).FirstOrDefault();
                var code = Suggest(label);
                // Property tables use column headers rather than labels down the left.
                var propertyName = propertyPos.Row > 0 && row > propertyPos.Row ? WorkbookValue.Text(s.GetValue(row, propertyPos.Column)) : "";
                var propertyEvidence = propertyName.Length > 0 && item.Value is not null;
                if (propertyPos.Row > 0 && row > propertyPos.Row)
                { label = WorkbookValue.Text(s.GetValue(propertyPos.Row, col)); code = Suggest(label); }
                var note = WorkbookValue.Text(item.Value);
                var isDateEvidence = label.Contains("Term Date", StringComparison.OrdinalIgnoreCase) || Regex.IsMatch(note, @"\bENDS?\s+20\d\d\b", RegexOptions.IgnoreCase);
                if (!(code.Length > 0 && period.Text is not null) && !isDateEvidence && !propertyEvidence) continue;
                var value = WorkbookValue.Decimal(item.Value);
                var state = isDateEvidence ? "Text evidence" : item.Value is null ? item.Formula is null ? "Missing" : "Missing formula cache" : note.StartsWith('#') ? "Formula error" : value.HasValue ? item.Formula is null ? "Numeric source" : "Formula cache; freshness unverified" : "Text evidence";
                candidates.Add(new() { SourceId = source.Id, Sheet = s.Name, Cell = cell, Label = label, RawText = note,
                    Formula = item.Formula ?? "", ValueState = state, SuggestedCode = isDateEvidence ? "" : code,
                    RawYear = period.Text ?? "", SuggestedProperty = propertyPos.Row > 0 ? WorkbookValue.Text(s.GetValue(row, propertyPos.Column)) : s.Name });
            }
        }
        source.Status = "Awaiting review";
    }
    public static string Suggest(string label)
    {
        var s = Regex.Replace(label.Trim().ToUpperInvariant(), @"\s+", " ");
        if (s == "TOTAL REVENUE") return "REVENUE";
        if (s == "TOTAL SOCIETY OPERATING EXPENSES") return "OPEX_SOCIETY";
        if (s.StartsWith("TOTAL BUILDING OPERATING EXPENSES")) return "OPEX_BUILDINGS";
        if (s is "TOTAL OPERATING EXPENSE" or "TOTAL OPERATING EXPENSES") return "OPEX";
        if (s.Contains("MORTGAGE") && s.Contains("PRINCIPAL") && s.Contains("PAYMENT")) return "DEBT_PRINCIPAL";
        if (s.Contains("MORTGAGE") && s.Contains("INTEREST") && s.Contains("PAYMENT")) return "DEBT_INTEREST";
        if (s == "MORTGAGE PAYMENTS") return "DEBT_SERVICE";
        if (s is "UNITS" or "TOTAL UNITS") return "HOMES_TOTAL";
        if (s.Contains("NET RESERVE FUNDS")) return "RESERVE_CLOSING";
        if (s == "SUBSIDY" || s.StartsWith("BC HOUSING SUBSIDY REVENUE")) return "REV_SUBSIDY";
        if (s.StartsWith("RENTAL REVENUE - ALL PROPERTY")) return "REV_RENT_EXISTING";
        if (s == "RENTAL REVENUE - NEW PROJECTS") return "REV_RENT_NEW";
        if (s == "TOTAL OTHER INCOME") return "REV_OTHER";
        return "";
    }
    private static WorkbookSnapshot Csv(string text)
    {
        var cells = new Dictionary<string, CellSnapshot>(); var field = new StringBuilder();
        var row = 1; var col = 1; var maxCol = 0; var quoted = false;
        void Put() { var t = field.ToString(); field.Clear(); object? value = t.Length == 0 ? null : decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out var n) ? n : t;
            cells[XlsxAddress.ToCellReference(row, col)] = new() { Value = value }; maxCol = Math.Max(maxCol, col); col++; }
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '"') { if (quoted && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; } else if (quoted || field.Length == 0) quoted = !quoted; else throw new InvalidDataException("Malformed CSV quote."); }
            else if (!quoted && c == ',') Put();
            else if (!quoted && c is '\n' or '\r') { if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++; Put(); row++; col = 1; }
            else field.Append(c);
            if (row > 100000 || col > 1000) throw new InvalidDataException("CSV exceeds demonstration limits.");
        }
        if (quoted) throw new InvalidDataException("Unclosed CSV quote.");
        if (field.Length > 0 || col > 1) Put();
        var sheet = new SheetSnapshot { Name = "CSV", Cells = cells, MaxRow = row, MaxColumn = maxCol };
        return new() { Sheets = new() { [sheet.Name] = sheet } };
    }
}
