using System.Globalization;
using System.Text.RegularExpressions;

namespace IreiMvp.Web;

// Historical observations enrich presentation only. They never become current financial inputs,
// occupied homes, verified loan instruments, or additional properties.
public static class BoardSupportingImporter
{
    private static string N(string text) => BoardWorkbookImporter.Norm(text);
    public static bool IsCashFlow(WorkbookSnapshot book) => book.Sheets.Values.Any(s =>
        BoardWorkbookImporter.Find(s, l => l == "TOTALREVENUE") > 0 &&
        BoardWorkbookImporter.Find(s, l => l.Contains("MORTGAGEPRINCIPAL")) > 0 &&
        BoardWorkbookImporter.Find(s, l => l.Contains("MORTGAGEINTEREST")) > 0);

    public static List<V4Row> Read(V4Assessment assessment, WorkbookSnapshot book, string fileName, List<string> findings)
    {
        var result = new List<V4Row>();
        var properties = assessment.Rows("31_PROPERTY").Where(p => p.Text("Include_in_Assessment") == "Yes").ToArray();
        var names = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        void Alias(string text, string id)
        {
            var key = N(text); if (key.Length == 0) return;
            if (!names.TryGetValue(key, out var ids)) names[key] = ids = [];
            ids.Add(id);
        }
        foreach (var p in properties)
        {
            Alias(p.Id, p.Id);
            foreach (var key in new[] { "Property_Name", "Address", "Street_Address", "Alias", "Aliases" })
                foreach (var alias in p.Text(key).Split(';', '|')) Alias(alias.Trim(), p.Id);
        }
        foreach (var note in assessment.Rows("12_SOURCE_NOTES"))
            if (properties.Any(p => p.Id == note.Text("Property_ID")))
                foreach (var key in new[] { "Alias", "Source_Alias", "Property_Name", "Source_Property_Name" })
                    Alias(note.Text(key), note.Text("Property_ID"));
        foreach (var note in assessment.Rows("12_SOURCE_NOTES").Where(r => r.Text("Related_Period") == "Name/address matching only"))
            if (properties.Any(p => p.Id == note.Text("Property_ID")))
                foreach (var alias in note.Text("Raw_Note_Text").Split(';', '/', '|')) Alias(alias.Trim(), note.Text("Property_ID"));
        string Role(string header) => N(header) switch
        {
            "PROPERTY" or "PROPERTYNAME" or "BUILDING" or "BUILDINGNAME" or "PROPERTYID" or "PROPERTYALIAS" or "NAME" => "Property",
            "UNITS" or "TOTALUNITS" or "LISTEDUNITS" or "NUMBEROFUNITS" or "NOOFUNITS" or "2025LISTEDUNITS" => "Units",
            "CITY" or "MUNICIPALITY" => "City",
            "ADDRESS" or "STREETADDRESS" or "PROPERTYADDRESS" => "Address",
            "MATURITY" or "MATURITYDATE" or "MORTGAGEMATURITY" or "MORTGAGEMATURITYDATE" or "RENEWALDATE" or "TERMENDDATE" or "MORTGAGETERMDATE" => "Maturity",
            "BALANCE" or "LOANBALANCE" or "MORTGAGEBALANCE" or "OUTSTANDINGPRINCIPAL" => "Balance",
            var label when Regex.IsMatch(label, @"^MORTGAGEBALANCEOUTSTANDING(?:20\d{2})?$") => "Balance",
            "MORTGAGE" or "LENDER" or "INSTRUMENT" => "Instrument",
            "BUILDDATE" or "YEARBUILT" => "Build_Date",
            "ASOF" or "ASOFDATE" or "SOURCEDATE" or "BALANCEDATE" => "Date",
            _ => ""
        };
        string Value(SheetSnapshot sheet, int row, int col, string role)
        {
            var value = BoardWorkbookImporter.Text(sheet, row, col);
            if ((role is "Date" or "Maturity") && WorkbookValue.Decimal(value) is decimal serial && serial is > 20000 and < 80000)
                return DateTime.FromOADate((double)serial).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return value;
        }
        foreach (var sheet in book.Sheets.Values)
        {
            var sheetDate = ""; var sheetDateSource = "";
            // Explicit update labels, not upload time or a year inferred from the file name.
            for (var r = 1; r <= Math.Min(sheet.MaxRow, 10); r++)
                for (var c = 1; c < sheet.MaxColumn; c++)
                    if (N(BoardWorkbookImporter.Text(sheet, r, c)) is "UPDATED" or "ASOF" or "SOURCEDATE")
                    {
                        var raw = Value(sheet, r, c + 1, "Date");
                        raw = Regex.Replace(raw, @"(\d{1,2})/(\d{4})", "$1, $2");
                        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                        { sheetDate = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); sheetDateSource = BoardWorkbookImporter.Location(sheet.Name, r, c + 1); }
                    }
            var headers = Enumerable.Range(1, Math.Min(sheet.MaxRow, 100)).Select(r => new {
                Row = r, Columns = Enumerable.Range(1, sheet.MaxColumn).Select(c => (Col: c, Role: Role(BoardWorkbookImporter.Text(sheet, r, c))))
                    .Where(c => c.Role.Length > 0).ToArray()
            }).Where(h => h.Columns.Count(c => c.Role == "Property") == 1 &&
                h.Columns.Any(c => c.Role is "Units" or "City" or "Maturity" or "Balance")).ToArray();
            foreach (var header in headers)
            {
                if (header.Columns.GroupBy(c => c.Role).Any(g => g.Count() > 1))
                { findings.Add(fileName + " · " + sheet.Name + ": repeated supporting column labels require mapping; no values selected."); continue; }
                var columns = header.Columns.ToDictionary(c => c.Role, c => c.Col);
                var end = headers.Where(h => h.Row > header.Row).Select(h => h.Row - 1).DefaultIfEmpty(sheet.MaxRow).Min();
                string? previousProperty = null; var previousLabel = "";
                for (var r = header.Row + 1; r <= end; r++)
                {
                    var label = BoardWorkbookImporter.Text(sheet, r, columns["Property"]);
                    if (N(label) is "TOTAL" or "TOTALS" or "GRANDTOTAL") { previousProperty = null; continue; }
                    var continuation = label.Length == 0 && previousProperty is not null && columns.TryGetValue("Instrument", out var instrumentColumn) &&
                        BoardWorkbookImporter.Text(sheet, r, instrumentColumn).Length > 0;
                    if (label.Length == 0 && !continuation) { previousProperty = null; continue; }
                    string propertyId;
                    if (continuation) propertyId = previousProperty!;
                    else
                    {
                        if (!names.TryGetValue(N(label), out var matches) || matches.Count != 1)
                        { previousProperty = null; findings.Add(fileName + " · " + BoardWorkbookImporter.Location(sheet.Name, r, columns["Property"]) + ": unmatched or ambiguous property '" + label + "'; no property was created."); continue; }
                        propertyId = matches.Single(); previousProperty = propertyId; previousLabel = label;
                    }
                    var fields = new List<(string Key, string Value)> {
                        ("Property_ID", propertyId), ("Source_File", fileName), ("Source_SHA256", book.Sha256),
                        ("Source_Label", continuation ? previousLabel : label), ("Review_State", "Historical supporting observation; current evidence unconfirmed"),
                        ("Source_Location", BoardWorkbookImporter.Location(sheet.Name, r, columns["Property"]))
                    };
                    if (!columns.ContainsKey("Date") && sheetDate.Length > 0) { fields.Add(("Date", sheetDate)); fields.Add(("Date_Source", sheetDateSource)); }
                    foreach (var (role, col) in columns.Where(c => c.Key != "Property"))
                    {
                        var value = Value(sheet, r, col, role);
                        // Preserve numeric zero; blank and invalid unit/balance observations remain unavailable.
                        if ((role is "Units" or "Balance") && WorkbookValue.Decimal(value) is null) value = "";
                        fields.Add((role, value));
                        fields.Add((role + "_Source", BoardWorkbookImporter.Location(sheet.Name, r, col)));
                    }
                    var build = fields.FirstOrDefault(f => f.Key == "Build_Date").Value;
                    var asOf = fields.FirstOrDefault(f => f.Key == "Date").Value;
                    var plannedListing = decimal.TryParse(build, out var buildYear) && DateTime.TryParse(asOf, out var sourceDate) && buildYear > sourceDate.Year;
                    fields.Add(("Listing_Kind", continuation ? "Loan continuation" : plannedListing ? "Planned listing" : "Historical listing"));
                    result.Add(BoardWorkbookImporter.Row("BOARD_HISTORICAL", "HIST-" + book.Sha256[..12] + "-" + result.Count, fields.ToArray()));
                }
            }
        }
        if (result.Count == 0) findings.Add(fileName + ": no supporting property rows could be mapped. Upload a table with Property name/ID and Units, City, Maturity date or Loan balance columns. Unrecognized layouts are retained, not guessed.");
        return result;
    }
}
