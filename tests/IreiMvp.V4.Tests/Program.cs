using System.Text.Json;
using IreiMvp.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

var root = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
var fixture = Path.Combine(root, "src/IreiMvp.Web/Data/Examples/IREI_Stage1_V4.2_NGOB_Real_Data_Test.xlsx");
var blank = Path.Combine(root, "src/IreiMvp.Web/Data/Templates/IREI_Stage1_MVP_V4.2_Live_Underwriting_Template_BLANK.xlsx");
var xlsx = new XlsxWorkbookService(); var importer = new V4WorkbookImporter(xlsx);
var a = importer.Read(fixture, Guid.NewGuid().ToString("N"), "fixture.xlsx");
var scope = a.Setting("PORTFOLIO_PROPERTY_ID"); var passed = 0;
void Check(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); passed++; Console.WriteLine("PASS: " + name); }
void Near(decimal? value, decimal expected, string name, decimal tolerance = .01m) => Check(value.HasValue && Math.Abs(value.Value - expected) <= tolerance, name);
decimal? M(string code, string year = "2026-2027") => a.Metric(scope, year, code)?.Value;
Near(M("REVENUE"), 12696486.415908122m, "FY27 revenue");
Near(M("OPEX"), 8614110.832749803m, "FY27 running costs");
Near(M("NOI"), 4082375.58315832m, "FY27 NOI");
Near(M("DEBT_SERVICE"), 2591047m, "FY27 annual loan payments");
Near(M("DSCR"), 1.5755698693070097m, "FY27 coverage", .000001m);
Near(M("CASH_AFTER_DEBT"), 1491328.5831583198m, "FY27 cash");
Near(M("RESERVE_CLOSING"), 2312308.7558795176m, "FY27 closing reserve");
Near(M("HOMES_TOTAL"), 715m, "FY27 projected homes");
Near(M("CASH_SHORTFALL"), 0m, "FY27 zero cash funding requirement");
Near(M("DSCR", "2029-2030"), .9958406776765482m, "FY30 precision", .000001m);
Near(M("CASH_SHORTFALL", "2029-2030"), 57113.97156585194m, "FY30 shortfall separate from cushion");
Near(M("PLACEHOLDER_SUBSIDY", "2029-2030"), 3318983m, "FY30 unique two-project subsidy");
Near(M("NOI_EX_PLACEHOLDER", "2029-2030"), 10355460.028434148m, "FY30 no-placeholder NOI");
Near(M("SHORTFALL_EX_PLACEHOLDER", "2029-2030"), 3376096.971565852m, "FY30 no-placeholder cash gap");
Near(M("DSCR_EX_PLACEHOLDER", "2029-2030"), .7541358950360945m, "FY30 no-placeholder coverage", .000001m);
Near(M("COVERAGE_GAP", "2029-2030"), 2803425.371566m, "FY30 separate target cushion");
Near(M("COVERAGE_GAP_EX_PLACEHOLDER", "2029-2030"), 6122408.371566m, "FY30 no-placeholder target cushion");
Check(a.Rows("31_PROPERTY").Count(r => r.Text("Include_in_Assessment") == "Yes") == 16, "16 properties");
Check(a.Rows("51_EXCEPTIONS").Count(r => r.Text("Status") == "Open") == 16, "16 open exceptions");
Check(a.Rows("51_EXCEPTIONS").Count(r => r.Text("Status") == "Resolved") == 2, "2 resolved exceptions");
Check(a.Rows("51_EXCEPTIONS").Count(r => r.Text("Status") == "Exception accepted") == 2, "2 accepted exceptions");
Check(a.Metric(scope, "2025-2026", "NOI")!.DisplayState == "Projected", "Budget remains Projected");
Check(a.Metric(scope, "2029-2030", "DSCR")!.Severity == "High", "Full-precision near-one coverage triggers High");
Check(V4CalculationEngine.Severity(.99m, 1.2m) == "High" && V4CalculationEngine.Severity(1m, 1.2m) == "Medium" &&
    V4CalculationEngine.Severity(1.19m, 1.2m) == "Medium" && V4CalculationEngine.Severity(1.2m, 1.2m) == "None", "Severity boundaries");
Check(V4ViewService.Format(a.Metric(scope, "2029-2030", "DSCR")) == "0.996x", "Near-one display preserves shortfall");
Check(a.Facts.First(f => f.Code == "REVENUE").InputCells.Length == 3, "Source-link formula chain resolves to raw numeric input");
Check(a.Facts.Any(f => f.MappingStatus == "Proposed") && a.Metrics.Any(m => m.ReviewState == "Under review"), "Pilot mappings stay proposed");
var snapshot = xlsx.Read(fixture);
var parity = 0;
for (var r = 7; r <= 259; r++)
{
    var s = snapshot.Sheets["41_METRIC_RESULTS"];
    var code = Convert.ToString(s.GetValue(r, 8)) ?? ""; var year = Convert.ToString(s.GetValue(r, 19)) ?? "";
    if (s.GetValue(r, 9) is not decimal expected) continue;
    var metric = a.Metric(scope, year, code); if (metric is null) throw new Exception("Missing registered metric: " + code);
    Near(metric.Value, expected, $"Workbook parity {year} {code}", code.Contains("DSCR") || code.Contains("SHARE") ? .000001m : .01m); parity++;
}
Check(parity > 200, "More than 200 workbook financial outputs reconciled");
var sensitivities = a.Rows("55_SENSITIVITY_INPUTS").ToArray();
for (var i = 0; i < sensitivities.Length; i++)
{
    var actual = JsonSerializer.SerializeToElement(V4ViewService.Sensitivity(a, sensitivities[i]));
    foreach (var (property, column) in new[] { ("Revenue", 9), ("Opex", 10), ("DebtService", 11), ("Noi", 12), ("Dscr", 13), ("RequiredNoi", 14), ("CoverageGap", 15), ("CashAfterDebt", 16) })
    {
        var expected = (decimal)snapshot.Sheets["56_SENSITIVITY_RESULTS"].GetValue(i + 7, column)!;
        Near(actual.GetProperty(property).GetDecimal(), expected, $"55/56 parity {sensitivities[i].Id} {property}", property == "Dscr" ? .000001m : .01m);
    }
}
foreach (var option in V4Analysis.Options(a))
{
    var actual = JsonSerializer.SerializeToElement(option);
    var row = a.Rows("57_OPTION_ANALYSIS").First(r => r.Id == actual.GetProperty("Id").GetString());
    foreach (var (property, column) in new[] { ("CashShortfallFunded", 5), ("ResidualCashShortfall", 6) })
    {
        var expected = snapshot.Sheets["57_OPTION_ANALYSIS"].GetValue(row.Row, column);
        if (expected is decimal amount) Near(actual.GetProperty(property).GetDecimal(), amount, "57 option parity " + row.Id + " " + property);
        else Check(actual.GetProperty(property).ValueKind == JsonValueKind.Null, "57 missing option amount stays missing " + row.Id + " " + property);
    }
}
JsonElement Test(V4OptionRequest r) => JsonSerializer.SerializeToElement(V4CalculationEngine.TestOption(a, r));
var bridge = Test(new("2029-2030", "Without placeholder", "Bridge", 3376096.971565852m));
Near(bridge.GetProperty("ResidualCashShortfall").GetDecimal(), 0m, "Bridge funds cash");
Near(bridge.GetProperty("CoverageAfter").GetDecimal(), .7541358950360945m, "Bridge does not improve coverage", .000001m);
var recurring = Test(new("2029-2030", "Without placeholder", "Recurring income", 3376096.971565852m));
Near(recurring.GetProperty("CoverageAfter").GetDecimal(), 1m, "Recurring net benefit restores break-even", .000001m);
var confirmed = Test(new("2029-2030", "Without placeholder", "Confirm subsidy", 0m, SubsidyConfirmed: true));
Near(confirmed.GetProperty("ResidualCashShortfall").GetDecimal(), 57113.97156585194m, "Subsidy confirmation restores once");
var baseConfirm = Test(new("2029-2030", "Base", "Confirm subsidy", 0m, SubsidyConfirmed: true));
Near(baseConfirm.GetProperty("Amount").GetDecimal(), 0m, "Subsidy confirmation never adds to base plan");
var originalDebt = M("DEBT_SERVICE");
a.Facts.Add(new("SECOND-DEBT", "PROP-MM", "2026-2027", "Projection", "DEBT_SERVICE", 999999m, "Available", "Property detail", "Under review", "Unmapped", "", "CAD", "TEST", "Test", "Test!A1", "", []));
V4CalculationEngine.Calculate(a); Near(M("DEBT_SERVICE"), originalDebt!.Value, "Property debt cannot change portfolio debt");
var saved = a.Facts.ToList();
a.Facts.RemoveAll(f => f.Scope == scope && f.Year == "2026-2027" && f.Code == "DEBT_SERVICE"); V4CalculationEngine.Calculate(a);
Check(M("DSCR") is null && a.Metric(scope, "2026-2027", "DSCR")!.Severity == "None", "Missing debt has no ratio/severity");
a.Facts = saved; V4CalculationEngine.Calculate(a);
var page = JsonSerializer.SerializeToElement(V4ViewService.Page(a, "P4"));
Check(page.GetProperty("Evidence")[0].GetProperty("State").GetString() == "Confirmed" && page.GetProperty("Evidence")[1].GetProperty("State").GetString() == "Partly confirmed", "Evidence period completeness independent of projection/review");
for (var p = 1; p <= 7; p++) Check(JsonSerializer.SerializeToElement(V4ViewService.Page(a, "P" + p)).GetProperty("Page").GetString() == "P" + p, "Page " + p + " view");
Check(JsonSerializer.SerializeToElement(V4ViewService.Page(a, "P1")).GetProperty("OutcomeState").GetString() == "Not yet measured", "No invented outcome zero");
Check(a.Rows("33_DEBT").All(r => r.Text("Maturity_Date") == ""), "Maturity tests explicitly waiting on data");
Check(JsonSerializer.SerializeToElement(V4Analysis.Capacity(a)).GetProperty("PreliminarySafeBorrowing").ValueKind == JsonValueKind.Null, "Missing critical capacity inputs withhold borrowing amount");
var continuity = JsonSerializer.SerializeToElement(V4Analysis.ReserveContinuity(a));
Near(continuity[0].GetProperty("Difference").GetDecimal(), -3609494.06m, "Reserve discontinuity computed from independent opening evidence");
Check(continuity[0].GetProperty("State").GetString() == "Reconciliation required", "Reserve difference remains a terminal review diagnostic");
var capacityCase = JsonSerializer.Deserialize<V4Assessment>(JsonSerializer.Serialize(a))!;
foreach (var r in capacityCase.Rows("58_CAPACITY_TESTS"))
{
    var value = r.Text("Editable assumption") switch { "Capacity fiscal year" => "2026-2027", "Minimum reserve floor" => "1000000", "Permission and control" => "Permitted", _ => null };
    if (value is not null) r.Fields["Value"] = value;
}
capacityCase.Registers["31_PROPERTY_VALUE"] = [new("VAL-TEST", "31_PROPERTY_VALUE", 0, new() {
    ["Assessment_ID"] = a.AssessmentId, ["Include_in_Capacity_Test"] = "Yes", ["Value_Amount"] = "10000000", ["Existing_Secured_Debt"] = "1000000" }, [])];
var cap = JsonSerializer.SerializeToElement(V4Analysis.Capacity(capacityCase));
Near(cap.GetProperty("PropertySecurity").GetDecimal(), 6500000m, "58 security formula");
Near(cap.GetProperty("AbilityToRepay").GetDecimal(), (4082375.58315832m / 1.2m - 2591047m) / .0773m, "58 repayment formula");
Near(cap.GetProperty("PreliminarySafeBorrowing").GetDecimal(), 1312308.7558795176m, "58 lowest complete capacity gate controls");
capacityCase.Rows("58_CAPACITY_TESTS").First(r => r.Text("Editable assumption") == "Permission and control").Fields["Value"] = "Restricted";
Near(JsonSerializer.SerializeToElement(V4Analysis.Capacity(capacityCase)).GetProperty("PreliminarySafeBorrowing").GetDecimal(), 0m, "58 restriction blocks preliminary borrowing");
try { importer.Read(blank, Guid.NewGuid().ToString("N"), "blank.xlsx"); throw new Exception("Blank accepted invented assessment"); }
catch (InvalidDataException) { Check(true, "Blank template creates no invented assessment"); }
// A second organization has its own IDs, currency, scope and labels. No organization defaults in code.
var second = JsonSerializer.Deserialize<V4Assessment>(JsonSerializer.Serialize(a))!;
second.Organization = "Second cooperative"; second.OrganizationId = "ORG-SECOND"; second.Config["ORGANIZATION_DISPLAY_NAME"] = second.Organization;
second.Config["PORTFOLIO_PROPERTY_ID"] = "SECOND-SCOPE";
second.Facts = second.Facts.Select(f => f with { Scope = f.Scope == scope ? "SECOND-SCOPE" : f.Scope }).ToList();
V4CalculationEngine.Calculate(second);
Check(second.Metric("SECOND-SCOPE", "2026-2027", "REVENUE")?.Value == M("REVENUE"), "Second organization calculation reuse");
Check(!JsonSerializer.Serialize(V4ViewService.Page(second, "P1")).Contains("First option to test: confirm NGO B"), "No NGO B text in generated solutions");
// Persisted immutable baseline, stale write protection and append-only decisions.
var temp = Path.Combine(Path.GetTempPath(), "irei-v4-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
try
{
    var secondPath = Path.Combine(temp, "second.xlsx"); File.Copy(blank, secondPath);
    var patches = new List<CellPatch>();
    void Put(string sheet, string cell, object value) => patches.Add(new CellPatch { Sheet = sheet, Cell = cell, Value = value });
    foreach (var (cell, value) in new[] { ("A7", "ASM-SECOND"), ("B7", "ORG-SECOND"), ("C7", "SECOND/1"), ("D7", "Working"), ("G7", "CAD"), ("H7", "V4.2") }) Put("01_ASSESSMENT", cell, value);
    foreach (var (cell, value) in new[] { ("A7", "ORG-SECOND"), ("B7", "Second Housing Cooperative"), ("F7", "CAD") }) Put("30_ORGANIZATION", cell, value);
    foreach (var (cell, value) in new[] { ("B7", "2026-2027"), ("B11", "2026-2027"), ("B12", "2026-2027"), ("B15", "SECOND-PORTFOLIO"), ("B18", "Second Housing Cooperative"), ("B23", "2026-2027"), ("B34", "Actual") }) Put("02_DISPLAY_CONFIG", cell, value);
    Put("10_SOURCE_REGISTER", "A7", "SRC-SECOND");
    foreach (var (cell, value) in new[] { ("A7", "SECOND-PROP"), ("B7", "ORG-SECOND"), ("C7", "Second Building"), ("I7", "Operating"), ("K7", "Yes") }) Put("31_PROPERTY", cell, value);
    for (var r = 7; r <= 8; r++)
    {
        foreach (var (column, value) in new[] { ("A", "SECOND-FIN-" + r), ("B", "ASM-SECOND"), ("C", "ORG-SECOND"), ("D", "SECOND-PORTFOLIO"), ("G", "2026-2027"), ("H", "Actual"), ("I", r == 7 ? "REVENUE" : "OPEX"), ("K", "CAD"), ("M", "SRC-SECOND"), ("N", "Second source row " + r), ("P", "Verified"), ("U", "Portfolio control"), ("V", "Ready") }) Put("32_FINANCIAL", column + r, value);
        Put("32_FINANCIAL", "J" + r, r == 7 ? 1000000m : 600000m);
    }
    foreach (var (cell, value) in new[] { ("A7", "SECOND-SCHEDULE"), ("B7", "ASM-SECOND"), ("C7", "SECOND-LOAN"), ("D7", "SECOND-PORTFOLIO"), ("G7", "2026-2027"), ("H7", "Actual"), ("N7", "CAD"), ("O7", "SRC-SECOND"), ("P7", "Second annual schedule"), ("Q7", "Verified") }) Put("33_DEBT_SCHEDULE", cell, value);
    Put("33_DEBT_SCHEDULE", "J7", 150000m); Put("33_DEBT_SCHEDULE", "K7", 50000m);
    xlsx.Patch(secondPath, patches);
    var importedSecond = importer.Read(secondPath, Guid.NewGuid().ToString("N"), "second.xlsx");
    Near(importedSecond.Metric("SECOND-PORTFOLIO", "2026-2027", "DSCR")?.Value, 2m, "Second organization imports its own input fixture", .000001m);
    Check(!JsonSerializer.Serialize(V4ViewService.Page(importedSecond, "P1")).Contains("NGO B"), "Second organization page contains no NGO B text");
    var store = new V4Store(importer, new TestEnvironment(temp));
    await using var source = File.OpenRead(fixture); var working = await store.ImportAsync(source, "fixture.xlsx", default);
    var baseline = JsonSerializer.Serialize(working.Baseline);
    working = await store.ConfigureAsync(working.Id, new("2025-2026", working.Version), default);
    Check(JsonSerializer.Serialize(working.Baseline) == baseline, "Baseline immutable after selected year change");
    Check(working.Setting("CURRENT_FISCAL_YEAR") == "2025-2026", "All current-year context shares one configuration");
    try { await store.ConfigureAsync(working.Id, new("2026-2027", 1), default); throw new Exception("Stale update accepted"); }
    catch (InvalidDataException) { Check(true, "Stale write rejected"); }
    working = await store.EditAsync(working.Id, new("48_ACTIONS", "ACTION-1", new() { ["Action_Text"] = "Confirm subsidy", ["Linked_Exception_IDs"] = "EXC-NGOB-001" }, "Analyst", "Source review", working.Version), default);
    Check(working.Rows("48_ACTIONS").Single().Text("Status") == "Proposed", "Action stays proposed");
    working = await store.EditAsync(working.Id, new("49_DECISIONS", "DEC-1", new() { ["Decision_Text"] = "Request commitment", ["Decision_Date"] = "2026-10-11", ["Decided_By"] = "Board", ["Decision_Status"] = "Approved", ["Implementation_Status"] = "In progress", ["Evidence_Record_IDs"] = "EXC-NGOB-001", ["Linked_Action_ID"] = "ACTION-1" }, "Approver", "Client decision evidence", working.Version), default);
    working = await store.ObserveAsync(working.Id, new("OBS-1", "ACTION-1", "DEC-1", "Homes protected", "Community", "Homes", "Increase", scope, "OUTCOME/1", 10m, null, "2026-2027", "SRC-NGOB-V18", "Client-reported contribution", "Analyst", working.Version), default);
    Check(working.Outcomes.Single().ValueState == "Not yet measured" && working.Outcomes.Single().CurrentValue is null, "Unmeasured outcome is null, never zero");
    working = await store.ObserveAsync(working.Id, new("OBS-2", "ACTION-1", "DEC-1", "Homes protected", "Community", "Homes", "Increase", scope, "OUTCOME/1", 10m, 12m, "2027-2028", "SRC-NGOB-V18", "Client-reported contribution", "Analyst", working.Version), default);
    Check(working.Outcomes.Last().Change == 2m, "Comparable observation calculates baseline change");
    working = await store.ObserveAsync(working.Id, new("OBS-3", "ACTION-1", "DEC-1", "Homes protected", "Community", "Homes", "Increase", "NEW-SCOPE", "OUTCOME/2", 10m, 15m, "2028-2029", "SRC-NGOB-V18", "Client-reported contribution", "Analyst", working.Version), default);
    Check(working.Outcomes.Last().Change is null, "Scope/method break suppresses misleading outcome delta");
    Check(JsonSerializer.Serialize(working.Baseline) == baseline, "Decision and outcome updates preserve original baseline");
    try { await store.EditAsync(working.Id, new("49_DECISIONS", "DEC-1", new(), "Approver", "Overwrite", working.Version), default); throw new Exception("Decision overwrite accepted"); }
    catch (InvalidDataException) { Check(true, "Approved decision append-only"); }
    Check(Directory.GetFiles(Path.Combine(temp, "App_Data/V4Assessments", working.Id, "Versions")).Length == working.Version, "Immutable version snapshots");
}
finally { Directory.Delete(temp, true); }
Console.WriteLine($"V4.2: {passed} assertions passed. Maturity/date-source acceptance remains WAITING ON DATA, not passed.");

sealed class TestEnvironment(string root) : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "Tests";
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    public string WebRootPath { get; set; } = root;
    public string EnvironmentName { get; set; } = "Development";
    public string ContentRootPath { get; set; } = root;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
