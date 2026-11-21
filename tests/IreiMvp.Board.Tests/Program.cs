using System.Text.Json;
using IreiMvp.Web;

// All observations in this regression are invented test data.
var assessment = new V4Assessment { OrganizationId = "TEST-ORG", AssessmentId = "TEST", Currency = "CAD",
    Config = new() { ["CURRENT_FISCAL_YEAR"] = "2026-2027", ["FULL_OPERATION_FISCAL_YEAR"] = "2027-2028",
        ["PORTFOLIO_PROPERTY_ID"] = "TEST-PORTFOLIO", ["HORIZON_FY_01"] = "2026-2027", ["HORIZON_SCENARIO_01"] = "Projection",
        ["HORIZON_FY_02"] = "2027-2028", ["HORIZON_SCENARIO_02"] = "Projection" }, Registers = new() {
    ["31_PROPERTY"] = [new("P-ONE", "31_PROPERTY", 0, new() { ["Organization_ID"] = "TEST-ORG", ["Property_Name"] = "Example House",
        ["Include_in_Assessment"] = "Yes", ["Operating_Status"] = "Planned" }, [])],
    ["12_SOURCE_NOTES"] = [new("ALIAS", "12_SOURCE_NOTES", 0, new() { ["Property_ID"] = "P-ONE",
        ["Related_Period"] = "Name/address matching only", ["Raw_Note_Text"] = "Old Example; New Example" }, [])]
} };
var book = new WorkbookSnapshot { Sha256 = new string('a', 64), Sheets = new() { ["Register"] = new() {
    Name = "Register", MaxRow = 11, MaxColumn = 7, Cells = new() {
        ["A1"] = new() { Value = "Updated" }, ["B1"] = new() { Value = "August 20/2025" },
        ["A4"] = new() { Value = "Property Name" }, ["B4"] = new() { Value = "Total Units" }, ["C4"] = new() { Value = "City" },
        ["D4"] = new() { Value = "Build Date" }, ["E4"] = new() { Value = "Mortgage" }, ["F4"] = new() { Value = "Mortgage Term Date" },
        ["G4"] = new() { Value = "Mortgage Balance Outstanding 2025" },
        ["A5"] = new() { Value = "Old Example" }, ["B5"] = new() { Value = 0m }, ["C5"] = new() { Value = "Example City" }, ["D5"] = new() { Value = 1986m },
        ["A6"] = new() { Value = "New Example" }, ["B6"] = new() { Value = 32m }, ["D6"] = new() { Value = 2028m },
        ["A7"] = new() { Value = "Example House" }, ["B7"] = new() { Value = 23m }, ["D7"] = new() { Value = 1970m },
        ["E7"] = new() { Value = "Lender A" }, ["F7"] = new() { Value = 46143m }, ["G7"] = new() { Value = 100m },
        ["E8"] = new() { Value = "Lender B" }, ["F8"] = new() { Value = 48000m }, ["G8"] = new() { Value = 50m },
        ["A9"] = new() { Value = "Unknown property" }, ["B9"] = new() { Value = 999m },
        ["E10"] = new() { Value = "Do not inherit an unmatched property" }, ["G10"] = new() { Value = 123m },
        ["B11"] = new() { Value = 2000m }, ["G11"] = new() { Value = 999999m }
    } } } };
void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS: " + name); }
var findings = new List<string>();
var history = BoardSupportingImporter.Read(assessment, book, "synthetic.xlsx", findings);
Check(history.Count == 4 && history.All(r => r.Text("Property_ID") == "P-ONE"), "Aliases retain a single property identity");
Check(history.All(r => r.Text("Date") == "2025-08-20" && r.Text("Date_Source") == "'Register'!B1"), "Explicit update date and original cell retained");
Check(history.Single(r => r.Text("Listing_Kind") == "Planned listing").Text("Units") == "32" &&
    history.Single(r => r.Text("Source_Label") == "Old Example").Text("Units") == "0", "Planned listing never overwrites historical zero");
Check(history.Single(r => r.Text("Instrument") == "Lender B").Text("Units") == "", "Continuation loan does not duplicate property units");
Check(findings.Count == 1 && !history.Any(r => r.Text("Balance") is "123" or "999999"), "Unmatched and total rows cannot inherit property context");
var view = JsonSerializer.SerializeToElement(BoardView.Build(new BoardRecord { Assessment = assessment, HistoricalProperties = history }));
Check(view.GetProperty("HistoricalTerms").GetProperty("Count").GetInt32() == 1 &&
    view.GetProperty("HistoricalTerms").GetProperty("CurrentBalance").GetDecimal() == 100m, "Near-term historical screen excludes later loans");
Check(assessment.Facts.Count == 0 && assessment.Rows("31_PROPERTY").Count() == 1, "Supporting observations never create financial inputs or additional properties");

var illustrationBook = new WorkbookSnapshot { Sheets = new() {
    ["Illustrative inputs"] = new() { Name = "Illustrative inputs", MaxRow = 11, MaxColumn = 4, Cells = new() {
        ["A8"] = new() { Value = "Assumption" }, ["B8"] = new() { Value = "Value" }, ["C8"] = new() { Value = "Unit" }, ["D8"] = new() { Value = "Status" },
        ["A9"] = new() { Value = "Existing homes" }, ["B9"] = new() { Value = 50m }, ["C9"] = new() { Value = "homes" }, ["D9"] = new() { Value = "Illustrative" },
        ["A10"] = new() { Value = "Vacancy" }, ["B10"] = new() { Value = .1m }, ["C10"] = new() { Value = "rate" }, ["D10"] = new() { Value = "Illustrative" } } },
    ["Executive Summary"] = new() { Name = "Executive Summary", MaxRow = 17, MaxColumn = 1, Cells = new() {
        ["A15"] = new() { Value = "Example assumptions · Illustrative" }, ["A16"] = new() { Value = "Example scenario text <script> is content, never executable HTML." } } }
} };
var demo = BoardIllustrativeImporter.Read(illustrationBook, "synthetic-illustration.xlsx");
Check(demo.Values.Count == 2 && demo.Values[0].Value == 50m && demo.Values.All(v => v.Status == "Illustrative"), "Only explicitly labelled illustrative assumptions are imported");
Check(demo.Values[0].Source == "synthetic-illustration.xlsx · 'Illustrative inputs'!B9" && demo.Sections.Single().Page == "P1", "Scenario provenance retains workbook, cell and page");
var demoView = JsonSerializer.SerializeToElement(BoardView.Build(new BoardRecord { Assessment = assessment, Illustrative = demo }));
Check(demoView.GetProperty("Illustrative").GetProperty("Values").GetArrayLength() == 2 && assessment.Facts.Count == 0,
    "Illustrative presentation never becomes verified evidence or financial facts");
illustrationBook.Sheets["Illustrative inputs"].Cells["D9"] = new() { Value = "Verified" };
try { BoardIllustrativeImporter.Read(illustrationBook, "invalid.xlsx"); throw new Exception("Unlabelled illustration accepted"); }
catch (InvalidDataException) { Check(true, "Illustrative assumptions cannot be relabelled verified"); }
illustrationBook.Sheets["Illustrative inputs"].Cells["D9"] = new() { Value = "Illustrative" };
illustrationBook.Sheets["Illustrative inputs"].Cells["B10"] = new() { Value = 2m };
try { BoardIllustrativeImporter.Read(illustrationBook, "invalid.xlsx"); throw new Exception("Invalid vacancy accepted"); }
catch (InvalidDataException) { Check(true, "Out-of-range illustrative rates rejected"); }
