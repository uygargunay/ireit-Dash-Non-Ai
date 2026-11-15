using System.Globalization;
namespace IreiMvp.Web;
public sealed partial class IntakeExtractor
{
    private static void ExtractMortgageLayouts(SheetSnapshot sheet, IntakeSource source, List<IntakeCandidate> output)
    {
        string Text(int row, int col) => WorkbookValue.Text(sheet.GetValue(row, col)).Trim();
        string Header(int col, int from, int to) => string.Join(' ', Enumerable.Range(from, to - from + 1).Select(r => Text(r, col))).ToUpperInvariant();
        void Add(int row, int col, string code, string property, string period, string label)
        {
            var address = XlsxAddress.ToCellReference(row, col); if (!sheet.Cells.TryGetValue(address, out var cell)) return;
            var raw = WorkbookValue.Text(cell.Value); if (string.IsNullOrWhiteSpace(raw) || raw.StartsWith('#')) return;
            if (code != "MORTGAGE_MATURITY" && !WorkbookValue.Decimal(cell.Value).HasValue) return;
            output.Add(new() { SourceId = source.Id, Sheet = sheet.Name, Cell = address, SuggestedCode = code, Label = label, RawText = raw,
                Formula = cell.Formula ?? "", SuggestedProperty = property, RawYear = period,
                ValueState = cell.Formula is null ? "Source observation; context unconfirmed" : "Formula cache; freshness unverified" });
        }
        // Mortgage registers with a multi-line header and one property/loan per row.
        var lenderColumn = Enumerable.Range(1, Math.Min(sheet.MaxColumn, 100)).FirstOrDefault(c => Header(c, 1, Math.Min(4, sheet.MaxRow)).Contains("MORTGAGE COMPANY"));
        if (lenderColumn > 1)
        {
            for (var row = 5; row <= Math.Min(sheet.MaxRow, 5000); row++)
            {
                var property = Text(row, lenderColumn - 1); if (property.Length == 0 || property.Contains("TOTAL", StringComparison.OrdinalIgnoreCase)) continue;
                for (var col = lenderColumn + 1; col <= Math.Min(sheet.MaxColumn, 100); col++)
                {
                    var header = Header(col, 1, Math.Min(4, sheet.MaxRow)); var code = ""; var period = "Unconfirmed";
                    if (header.Contains("ANNUAL") && header.Contains("PAYMENT")) code = "DEBT_SERVICE";
                    else if (header.Contains("EXPIRE ON")) code = "MORTGAGE_MATURITY";
                    else if (header.Trim() == "RATE") code = "MORTGAGE_RATE";
                    else if (DateTime.TryParseExact(Text(2, col), new[] { "MMM dd/yy", "MMM d/yy", "MMM dd/yyyy", "yyyy-MM-dd" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                    { code = "DEBT_BALANCE"; period = Text(2, col); }
                    if (code.Length > 0) Add(row, col, code, property, period, header);
                }
            }
        }
        // Annual payment matrices: dates down the first column and property groups across columns.
        var mortgageHeader = Enumerable.Range(1, Math.Min(8, sheet.MaxRow)).FirstOrDefault(r => Enumerable.Range(1, Math.Min(sheet.MaxColumn, 100)).Any(c => Text(r,c).Equals("Total Mortgage Payments", StringComparison.OrdinalIgnoreCase)));
        if (mortgageHeader == 0) return;
        for (var row = mortgageHeader + 1; row <= Math.Min(sheet.MaxRow, 5000); row++)
        {
            var period = Text(row, 1); if (!DateTime.TryParseExact(period, new[] { "MMM dd/yy", "MMM d/yy", "MMM dd/yyyy", "yyyy-MM-dd" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) continue;
            var property = "";
            for (var col = 2; col <= Math.Min(sheet.MaxColumn, 100); col++)
            {
                if (Text(1, col).Length > 0) property = Text(1,col);
                var h = Text(mortgageHeader, col).ToUpperInvariant();
                var code = h switch { "PRINCIPAL" => "DEBT_PRINCIPAL", "INTEREST" => "DEBT_INTEREST", "LOAN AMOUNT" => "DEBT_BALANCE", "TOTAL MORTGAGE PAYMENTS" => "DEBT_SERVICE", _ => "" };
                if (h.Length == 0 && col > 3 && Text(mortgageHeader,col-1).Equals("Interest",StringComparison.OrdinalIgnoreCase) && Text(mortgageHeader,col-2).Equals("Principal",StringComparison.OrdinalIgnoreCase)) code = "DEBT_SERVICE";
                if (code.Length > 0) Add(row, col, code, property.Length>0 ? property : sheet.Name, period, code + " / " + property);
            }
        }
    }
}
