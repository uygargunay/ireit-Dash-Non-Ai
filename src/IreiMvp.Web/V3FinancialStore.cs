using System.Text.Json;

namespace IreiMvp.Web;

// V3.1 structured-template path. The original workbook is retained; derived values never enter the fact list.
public sealed class V3FinancialStore(XlsxWorkbookService xlsx, IWebHostEnvironment environment)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string Root => Path.Combine(environment.ContentRootPath, "App_Data", "V3Assessments");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private const decimal CoverageTarget = 1.20m;

    public async Task<V3Assessment> ImportAsync(Stream input, string fileName, CancellationToken ct)
    {
        var id = Guid.NewGuid().ToString("N");
        var folder = Path.Combine(Root, id);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "source.xlsx");
        await using (var output = File.Create(path)) await input.CopyToAsync(output, ct);
        try
        {
            var workbook = xlsx.Read(path);
            if (workbook.HasSheet("32_FINANCIAL") && workbook.HasSheet("33_DEBT_SCHEDULE"))
            {
                var internalResult = ImportInternal(workbook, id, fileName);
                await SaveAsync(internalResult, ct);
                return internalResult;
            }
            if (!workbook.HasSheet("Assessment") || !workbook.HasSheet("Financials"))
                throw new InvalidDataException("V3.1 requires a supplied customer or internal template. Other Excel models need analyst mapping and cannot be auto-approved.");
            var view = new WorkbookDataView(workbook);
            var context = view.Rows("Assessment").FirstOrDefault()
                ?? throw new InvalidDataException("Complete the Assessment sheet before upload.");
            var name = context.Text("Organization_Legal_Name *", "Organization_Legal_Name");
            var currency = context.Text("Default_Currency *", "Default_Currency");
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(currency))
                throw new InvalidDataException("Organization legal name and default currency are required.");
            var result = new V3Assessment
            {
                Id = id, Organization = name, Currency = currency, FileName = Path.GetFileName(fileName),
                Sha256 = workbook.Sha256, CreatedUtc = DateTimeOffset.UtcNow,
                MethodologyVersion = "V3.1-PILOT", RuleVersion = "RULE-COVERAGE/1.1"
            };
            var properties = view.Rows("Properties").Select(p => p.Text("Property_ID *", "Property_ID"))
                .Where(p => p.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var sourceIds = view.Rows("Sources").Select(s => s.Text("Source_ID *", "Source_ID"))
                .Where(s => s.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var row in view.Rows("Financials"))
            {
                var property = row.Text("Property_ID *", "Property_ID");
                var period = row.Text("Fiscal_Year *", "Fiscal_Year");
                var scenario = row.Text("Scenario *", "Scenario");
                var metric = row.Text("Metric_Code *", "Metric_Code").ToUpperInvariant();
                if (property.Length == 0 || period.Length == 0 || scenario.Length == 0) continue;
                // Detail and subtotal rows are staging evidence, not additive financial controls.
                if (metric is not ("REVENUE" or "OPEX" or "DEBT_SERVICE" or "RESERVE_CONTRIBUTION")) continue;
                var amount = row.Decimal("Amount *", "Amount");
                if (amount is null) { result.Exceptions.Add($"Financials row {row.RowNumber}: {metric} amount is Missing; dependent metrics were not calculated."); continue; }
                var source = row.Text("Source_ID *", "Source_ID");
                if (!properties.Contains(property) || !sourceIds.Contains(source) ||
                    !string.Equals(row.Text("Currency *", "Currency"), currency, StringComparison.OrdinalIgnoreCase))
                { result.Exceptions.Add($"Financials row {row.RowNumber}: unresolved property, source, or currency. Fact excluded."); continue; }
                result.Facts.Add(new V3Fact(row.Text("Record_ID"), property, period, scenario, metric, amount.Value,
                    source, row.Text("Source_Location"), $"Financials!I{row.RowNumber}"));
            }
            foreach (var row in view.Rows("Debt"))
            {
                var property = row.Text("Property_ID *", "Property_ID");
                var service = row.Decimal("Current_Annual_Debt_Service");
                if (service is null || !properties.Contains(property)) continue;
                result.Exceptions.Add($"Debt row {row.RowNumber}: annual service has no confirmed fiscal-year/scenario schedule; excluded from period DSCR.");
            }
            // The supplied NGOB customer sheet declares the Existing documents route. Its source
            // year labels must be checked against the original workbook before facts are approved.
            if (context.Text("Submission_Route *", "Submission_Route").Contains("Existing documents", StringComparison.OrdinalIgnoreCase))
            {
                result.Status = "Mapping Required";
                result.Exceptions.Add("Existing-document route: source headers and mappings require analyst confirmation. Financial controls are staged; Page 3 is Not Assessed.");
            }
            else Calculate(result, []);
            await SaveAsync(result, ct);
            return result;
        }
        catch { Directory.Delete(folder, true); throw; }
    }

    private static V3Assessment ImportInternal(WorkbookSnapshot workbook, string id, string fileName)
    {
        var view = new WorkbookDataView(workbook);
        var organization = view.Rows("30_ORGANIZATION").FirstOrDefault()
            ?? throw new InvalidDataException("Internal template has no organization record.");
        var result = new V3Assessment
        {
            Id = id, Organization = organization.Text("Legal_Name *", "Legal_Name"),
            Currency = organization.Text("Default_Currency *", "Default_Currency"),
            FileName = Path.GetFileName(fileName), Sha256 = workbook.Sha256, CreatedUtc = DateTimeOffset.UtcNow,
            MethodologyVersion = "V3.1-PILOT", RuleVersion = "RULE-COVERAGE/1.1"
        };
        foreach (var row in view.Rows("32_FINANCIAL"))
        {
            var status = row.Text("Review_Status *", "Review_Status");
            if (!status.StartsWith("Confirmed", StringComparison.OrdinalIgnoreCase)) continue;
            var metric = row.Text("Metric_Code *", "Metric_Code").ToUpperInvariant();
            if (metric is not ("REVENUE" or "OPEX" or "RESERVE_CONTRIBUTION" or "DEBT_SERVICE_SOURCE")) continue;
            var amount = row.Decimal("Amount *", "Amount");
            if (!amount.HasValue) continue;
            result.Facts.Add(new V3Fact(row.Text("Financial_Record_ID *", "Financial_Record_ID"),
                row.Text("Property_ID *", "Property_ID"), row.Text("Fiscal_Year *", "Fiscal_Year"),
                row.Text("Scenario *", "Scenario"), metric, amount.Value,
                row.Text("Source_ID *", "Source_ID"), row.Text("Source_Location"), $"32_FINANCIAL!J{row.RowNumber}"));
        }
        var schedules = new List<V3Fact>();
        foreach (var row in view.Rows("33_DEBT_SCHEDULE"))
        {
            if (!row.Text("Review_Status *", "Review_Status").StartsWith("Confirmed", StringComparison.OrdinalIgnoreCase)) continue;
            var principal = row.Decimal("Principal_Payment");
            var interest = row.Decimal("Interest_Payment");
            // Recompute from the two source inputs. A formula cache is neither a fact nor a trusted calculator.
            if (!principal.HasValue || !interest.HasValue) continue;
            schedules.Add(new V3Fact(row.Text("Debt_Schedule_ID *", "Debt_Schedule_ID"),
                row.Text("Property_ID *", "Property_ID"), row.Text("Fiscal_Year *", "Fiscal_Year"),
                row.Text("Scenario *", "Scenario"), "DEBT_SERVICE", principal.Value + interest.Value,
                row.Text("Source_ID *", "Source_ID"), row.Text("Source_Location"),
                $"33_DEBT_SCHEDULE!J{row.RowNumber}:K{row.RowNumber}"));
        }
        Calculate(result, schedules);
        return result;
    }

    private static void Calculate(V3Assessment a, IReadOnlyList<V3Fact> schedules)
    {
        foreach (var group in a.Facts.GroupBy(f => (f.PropertyId, f.FiscalYear, f.Scenario)))
        {
            var facts = group.ToList();
            decimal? Sum(string code) => facts.Any(f => f.MetricCode == code)
                ? facts.Where(f => f.MetricCode == code).Sum(f => f.Amount) : null;
            var revenue = Sum("REVENUE"); var opex = Sum("OPEX");
            var prefix = group.Key;
            var debtFacts = schedules.Where(f => f.PropertyId == prefix.PropertyId && f.FiscalYear == prefix.FiscalYear && f.Scenario == prefix.Scenario).ToList();
            // Source-reported debt service is a comparison control, not a substitute for
            // the reviewed annual debt schedule. Customer annual controls are usable.
            if (debtFacts.Count == 0) debtFacts = facts.Where(f => f.MetricCode == "DEBT_SERVICE").ToList();
            void Add(string code, decimal? value, string calc, string state = "Missing")
            {
                var inputs = code switch
                {
                    "REVENUE" or "NOI_MARGIN" => facts.Where(f => f.MetricCode == "REVENUE"),
                    "OPEX" => facts.Where(f => f.MetricCode == "OPEX"),
                    "CASH_AFTER_RESERVE" => facts.Where(f => f.MetricCode is "REVENUE" or "OPEX" or "RESERVE_CONTRIBUTION").Concat(debtFacts),
                    "DSCR" or "CASH_AFTER_DEBT" or "DEBT_BURDEN" or "REQUIRED_NOI" or "COVERAGE_GAP" =>
                        facts.Where(f => f.MetricCode is "REVENUE" or "OPEX").Concat(debtFacts),
                    _ => facts.Where(f => f.MetricCode is "REVENUE" or "OPEX")
                };
                a.Metrics.Add(new V3Metric(prefix.PropertyId, prefix.FiscalYear, prefix.Scenario, code, value,
                    value.HasValue ? "Available" : state, calc, "1.0", inputs.Select(f => f.Id).Where(s => s.Length > 0).ToArray(),
                    inputs.Select(f => $"{f.SourceId}: {f.SourceLocation} ({f.Cell})").Distinct().ToArray()));
            }
            Add("REVENUE", revenue, "CALC-REVENUE"); Add("OPEX", opex, "CALC-OPEX");
            var noi = revenue.HasValue && opex.HasValue ? revenue - opex : null;
            Add("NOI", noi, "CALC-NOI");
            Add("NOI_MARGIN", revenue is > 0 && noi.HasValue ? noi / revenue : null, "CALC-NOI-MARGIN",
                revenue == 0 ? "Not Applicable" : "Missing");
            // The customer debt register has no annual period key. Never attach its current value to every year.
            var signUnresolved = a.Facts.Any(f => f.PropertyId == prefix.PropertyId &&
                f.MetricCode == "DEBT_SERVICE_SOURCE" && f.Amount < 0);
            var explicitZeroDebt = !signUnresolved && debtFacts.Count == 1 && debtFacts[0].Amount == 0;
            var debt = !signUnresolved && debtFacts.Count == 1 && debtFacts[0].Amount >= 0
                ? debtFacts[0].Amount : (decimal?)null;
            if (debtFacts.Count > 1 || debtFacts.Any(f => f.Amount < 0) || signUnresolved)
                a.Exceptions.Add($"{prefix.PropertyId} {prefix.FiscalYear}: debt service sign or duplicate source requires review; DSCR is Missing.");
            a.Metrics.Add(new V3Metric(prefix.PropertyId, prefix.FiscalYear, prefix.Scenario, "DEBT_SERVICE", debt,
                debt.HasValue ? "Available" : debtFacts.Count == 0 && !signUnresolved ? "Missing" : "Conflict", "CALC-DEBT-SVC", "1.0",
                debtFacts.Select(f => f.Id).ToArray(), debtFacts.Select(f => $"{f.SourceId}: {f.SourceLocation} ({f.Cell})").ToArray()));
            var dscr = noi.HasValue && debt is > 0 ? noi / debt : null;
            Add("DSCR", dscr, "CALC-DSCR", explicitZeroDebt ? "Not Applicable" : "Missing");
            Add("CASH_AFTER_DEBT", noi.HasValue && debt.HasValue ? noi - debt : null, "CALC-CASH-AFTER-DEBT");
            var reserve = Sum("RESERVE_CONTRIBUTION");
            Add("CASH_AFTER_RESERVE", noi.HasValue && debt.HasValue && reserve.HasValue ? noi - debt - reserve : null,
                "CALC-CASH-AFTER-RESERVE", "Not Assessed");
            Add("DEBT_BURDEN", revenue is > 0 && debt.HasValue ? debt / revenue : null, "CALC-DEBT-BURDEN",
                revenue == 0 ? "Not Applicable" : "Missing");
            Add("REQUIRED_NOI", debt.HasValue ? debt * CoverageTarget : null, "CALC-REQUIRED-NOI");
            Add("COVERAGE_GAP", debt.HasValue && noi.HasValue ? debt * CoverageTarget - noi : null, "CALC-COVERAGE-GAP");
            if (dscr.HasValue)
                a.Issues.Add(new V3Issue(prefix.PropertyId, prefix.FiscalYear, prefix.Scenario, "RULE-COVERAGE", "1.1",
                    dscr < CoverageTarget, dscr < 1.00m ? "High" : dscr < CoverageTarget ? "Medium" : "None"));
        }
    }

    private static decimal? Conflict(V3Assessment a, string property, string year, string message)
    { a.Exceptions.Add($"{property} {year}: {message}; analyst review required."); return null; }

    public async Task<V3Assessment?> GetAsync(string id, CancellationToken ct)
    {
        if (!Guid.TryParseExact(id, "N", out _)) return null;
        var path = Path.Combine(Root, id, "assessment.json");
        return File.Exists(path) ? JsonSerializer.Deserialize<V3Assessment>(await File.ReadAllTextAsync(path, ct), Json) : null;
    }

    public async Task<V3Assessment?> ApproveAsync(string id, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var assessment = await GetAsync(id, ct);
            if (assessment is null) return null;
            if (assessment.Exceptions.Count > 0 || assessment.Metrics.Count == 0)
                throw new InvalidDataException("Resolve the import exceptions and confirm the required methodology before approval.");
            // V3.1 pilot methodology is not production approved yet.
            throw new InvalidDataException("Production approval is blocked until an authorized methodology owner approves V3.1 rules and the source evidence.");
        }
        finally { _gate.Release(); }
    }

    private async Task SaveAsync(V3Assessment assessment, CancellationToken ct) =>
        await File.WriteAllTextAsync(Path.Combine(Root, assessment.Id, "assessment.json"),
            JsonSerializer.Serialize(assessment, Json), ct);
}

public sealed class V3Assessment
{
    public string Id { get; set; } = "";
    public string Organization { get; set; } = "";
    public string Currency { get; set; } = "";
    public string FileName { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public DateTimeOffset CreatedUtc { get; set; }
    public string Status { get; set; } = "Working";
    public string MethodologyVersion { get; set; } = "";
    public string RuleVersion { get; set; } = "";
    public List<V3Fact> Facts { get; set; } = [];
    public List<V3Metric> Metrics { get; set; } = [];
    public List<V3Issue> Issues { get; set; } = [];
    public List<string> Exceptions { get; set; } = [];
}

public record V3Fact(string Id, string PropertyId, string FiscalYear, string Scenario, string MetricCode,
    decimal Amount, string SourceId, string SourceLocation, string Cell);
public record V3Metric(string PropertyId, string FiscalYear, string Scenario, string MetricCode, decimal? Value,
    string State, string CalcId, string CalcVersion, string[] InputRecordIds, string[] Sources);
public record V3Issue(string PropertyId, string FiscalYear, string Scenario, string RuleId, string RuleVersion,
    bool Triggered, string Severity);
