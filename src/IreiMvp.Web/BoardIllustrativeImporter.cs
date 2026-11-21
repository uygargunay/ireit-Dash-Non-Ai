namespace IreiMvp.Web;

public sealed record BoardIllustrativeValue(string Label, decimal Value, string Unit, string Status, string Source);
public sealed record BoardIllustrativeSection(string Page, string Title, string Body, string Status, string Source);
public sealed record BoardIllustrativeMortgageCase(decimal Rate, int Years, string Status, string Source);
public sealed class BoardIllustrativeData
{
    public List<BoardIllustrativeValue> Values { get; set; } = [];
    public List<BoardIllustrativeSection> Sections { get; set; } = [];
    public List<BoardIllustrativeMortgageCase> MortgageCases { get; set; } = [];
}

// Deliberate demonstration inputs stay separate from financial facts, verified
// evidence, board approvals and baseline/outcome records.
public static class BoardIllustrativeImporter
{
    public static BoardIllustrativeData Read(WorkbookSnapshot book, string fileName)
    {
        var result = new BoardIllustrativeData();
        if (!book.Sheets.TryGetValue("Illustrative inputs", out var inputs)) return result;
        string Text(SheetSnapshot sheet, int row, int col) => BoardWorkbookImporter.Text(sheet, row, col);
        var header = Enumerable.Range(1, Math.Min(inputs.MaxRow, 30)).FirstOrDefault(r =>
            Text(inputs, r, 1) == "Assumption" && Text(inputs, r, 2) == "Value" &&
            Text(inputs, r, 3) == "Unit" && Text(inputs, r, 4) == "Status");
        if (header == 0) return result;
        for (var row = header + 1; row <= Math.Min(inputs.MaxRow, header + 250); row++)
        {
            var label = Text(inputs, row, 1);
            if (label.Length == 0) continue;
            if (Text(inputs, row, 4) != "Illustrative")
                throw new InvalidDataException($"Illustrative input '{label}' must retain its Illustrative status.");
            // Formula caches and duplicate labels cannot silently replace an assumption.
            var cell = inputs.GetCell(row, 2);
            var value = WorkbookValue.Decimal(cell?.Value);
            if (cell?.Formula is { Length: > 0 } || value is null || result.Values.Any(v => v.Label == label))
                throw new InvalidDataException($"Illustrative input '{label}' needs one unique, literal numeric value.");
            var unit = Text(inputs, row, 3);
            if (value < 0 && unit != "CAD/year" || unit == "rate" && (value < 0 || value > 1) ||
                (unit is "homes" or "people" or "children" or "years") && (value < 0 || value != decimal.Truncate(value.Value)))
                throw new InvalidDataException($"Illustrative input '{label}' has an invalid value for {unit}.");
            result.Values.Add(new(label, value.Value, unit, "Illustrative", fileName + " · " + BoardWorkbookImporter.Location(inputs.Name, row, 2)));
        }
        if (result.Values.Count == 0) return result;
        foreach (var (name, page) in new[] { ("Executive Summary", "P1"), ("Portfolio & Mission", "P2"),
            ("Mission and Stewardship", "P2"), ("Risks and Options", "P5") })
        {
            if (!book.Sheets.TryGetValue(name, out var sheet)) continue;
            for (var row = 1; row < Math.Min(sheet.MaxRow, 250); row++)
            {
                var title = Text(sheet, row, 1); var body = Text(sheet, row + 1, 1);
                if (!title.EndsWith("Illustrative", StringComparison.Ordinal) || !title.Contains('·') || body.Length == 0) continue;
                result.Sections.Add(new(page, title[..title.LastIndexOf('·')].Trim(), body, "Illustrative",
                    fileName + " · " + BoardWorkbookImporter.Location(name, row + 1, 1)));
            }
        }
        if (book.Sheets.TryGetValue("Risks and Options", out var risks))
        {
            var casesHeader = Enumerable.Range(1, Math.Min(risks.MaxRow, 100)).FirstOrDefault(r =>
                Text(risks, r, 1) == "Rate" && Text(risks, r, 2) == "Amort. years");
            if (casesHeader > 0)
                for (var row = casesHeader + 1; row <= Math.Min(risks.MaxRow, casesHeader + 10); row++)
                {
                    var rate = WorkbookValue.Decimal(risks.GetValue(row, 1));
                    var years = WorkbookValue.Decimal(risks.GetValue(row, 2));
                    if (rate is null || years is null) break;
                    if (rate < 0 || rate > 1 || years < 1 || years > 100 || years != decimal.Truncate(years.Value) ||
                        risks.GetCell(row, 1)?.Formula is { Length: > 0 } || risks.GetCell(row, 2)?.Formula is { Length: > 0 } ||
                        Text(risks, row, 9) != "Illustrative")
                        throw new InvalidDataException("Mortgage comparison cases require literal rates, integer years and Illustrative status.");
                    result.MortgageCases.Add(new(rate.Value, (int)years.Value, "Illustrative", fileName + " · " + BoardWorkbookImporter.Location(risks.Name, row, 1)));
                }
        }
        return result;
    }
}
