using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace IreiMvp.Web;

public static class EvidenceAssessmentBuilder
{
    public static readonly HashSet<string> Supported = ["REVENUE", "OPEX", "OPEX_SOCIETY", "OPEX_BUILDINGS", "DEBT_SERVICE", "DEBT_PRINCIPAL", "DEBT_INTEREST",
        "RESERVE_CLOSING", "HOMES_TOTAL", "REV_SUBSIDY", "REV_RENT_EXISTING", "REV_RENT_NEW", "REV_OTHER"];
    public static V4Assessment Build(V4WorkbookImporter importer, string root, EvidenceTenant tenant, EvidenceProperty[] properties,
        EvidenceFact[] facts, EvidenceVersion[] versions, EvidenceCreateAssessment input, string[] warnings)
    {
        var id = Guid.NewGuid().ToString("N");
        var a = importer.ReadStructure(Path.Combine(root, "Data", "Templates", "IREI_Stage1_MVP_V4.2_Live_Underwriting_Template_BLANK.xlsx"), id, "Reviewed portal evidence");
        // Retain only display/method definitions. Never inherit blank-template examples, financial inputs or assumptions.
        a.Registers = a.Registers.Where(r => new[] { "04_PAGE_REGISTRY", "05_SECTION_REGISTRY", "40_CALC_REGISTRY", "53_EVIDENCE_STATUS" }.Contains(r.Key)).ToDictionary(r => r.Key, r => r.Value);
        a.AssessmentId = "EVID-" + id; a.OrganizationId = tenant.Id; a.Organization = tenant.Name; a.Currency = input.Currency;
        a.Sha256 = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { Facts = facts, Sources = versions.Select(v => new { v.Id, v.Hash }) })));
        var portfolio = "PORTFOLIO-" + tenant.Id; var years = facts.Select(f => f.Period).Distinct().Order().ToArray();
        a.Config = new() { ["PORTFOLIO_PROPERTY_ID"] = portfolio, ["CURRENT_FISCAL_YEAR"] = input.CurrentYear,
            ["FULL_OPERATION_FISCAL_YEAR"] = input.FullOperationYear, ["HORIZON_END_FISCAL_YEAR"] = years.Last(), ["SHOW_DRAFT_NARRATIVE"] = "Yes", ["EVIDENCE_SOURCE_VERSION"] = "Reviewed evidence snapshot", ["REPORTING_AS_OF_DATE"] = tenant.ReportingCutoff };
        if (input.TargetCoverage.HasValue) a.Config["TARGET_COVERAGE"] = input.TargetCoverage.Value.ToString(CultureInfo.InvariantCulture);
        for (var i = 0; i < years.Length; i++) { a.Config[$"HORIZON_FY_{i + 1:D3}"] = years[i]; a.Config[$"HORIZON_SCENARIO_{i + 1:D3}"] = input.Basis.EndsWith("actual", StringComparison.OrdinalIgnoreCase) ? "Actual" : input.Basis; }
        void Row(string sheet, string rowId, params (string Key, string Value)[] fields)
        { if (!a.Registers.ContainsKey(sheet)) a.Registers[sheet] = []; a.Registers[sheet].Add(new(rowId, sheet, 0, fields.ToDictionary(p => p.Key, p => p.Value), [])); }
        void Issue(string description)
        { Row("51_EXCEPTIONS", "ISS-" + Guid.NewGuid().ToString("N"), ("Assessment_ID", a.AssessmentId), ("Description", description), ("Status", "Open"), ("Materiality", "Material"), ("Show_On_Evidence_Page", "Yes")); }
        foreach (var v in versions)
        { Row("10_SOURCE_REGISTER", v.Id, ("Source_ID", v.Id), ("Source_Name", v.Name), ("File_Name", v.Name), ("Version", v.Number.ToString(CultureInfo.InvariantCulture)), ("SHA256", v.Hash), ("Authoritative_Status", "Selected reviewed source")); }
        foreach (var p in properties.Where(p => facts.Any(f => f.PropertyId == p.Id)))
            Row("31_PROPERTY", p.Id, ("Property_ID", p.Id), ("Organization_ID", tenant.Id), ("Property_Name", p.Name), ("Short_Name", p.Name), ("Address", p.Address), ("Include_in_Assessment", p.InScope ? "Yes" : "No"), ("Source_ID", p.EvidenceVersionId));
        foreach (var f in facts)
        {
            var scope = f.PropertyId.Length == 0 ? portfolio : f.PropertyId;
            var scenario = input.Basis.EndsWith("actual", StringComparison.OrdinalIgnoreCase) ? "Actual" : input.Basis;
            var duplicate = facts.Count(o => o.PropertyId == f.PropertyId && o.Period == f.Period && o.MetricCode == f.MetricCode) > 1;
            var signProblem = f.Value < 0 && f.MetricCode is "OPEX" or "OPEX_SOCIETY" or "OPEX_BUILDINGS" or "DEBT_SERVICE" or "DEBT_PRINCIPAL" or "DEBT_INTEREST";
            if (signProblem) Issue($"{scope} / {f.Period}: {f.MetricCode} has a negative source sign. Confirm the positive expense/payment convention with a traceable correction; the assessment does not silently reverse it.");
            var conflict = duplicate || signProblem;
            a.Facts.Add(new(f.Id, scope, f.Period, scenario, f.MetricCode, conflict ? null : f.Value, conflict ? "Conflict" : "Available",
                scope == portfolio ? "Portfolio control" : "Property detail", "Verified", "Confirmed", f.Certainty == "Estimated" ? "Placeholder" : f.Certainty,
                input.Currency, f.DocumentVersionId, f.Locator, f.Locator, "", [f.DocumentVersionId + ":" + f.Locator]));
            Row("11_RAW_STAGING", f.Id, ("Assessment_ID", a.AssessmentId), ("Raw_Label", f.SourceLabel), ("Raw_Value_Number", f.Value?.ToString(CultureInfo.InvariantCulture) ?? ""), ("Source_ID", f.DocumentVersionId), ("Source_Location", f.Locator), ("Context_Text", f.Period));
            Row("20_FIELD_MAPPING", "MAP-" + f.Id, ("Mapping_ID", "MAP-" + f.Id), ("Mapping_Status", conflict ? "Conflict" : "Confirmed"), ("Source_ID", f.DocumentVersionId), ("Source_Location", f.Locator), ("Metric_Code", f.MetricCode), ("Canonical_Entity", scope), ("Canonical_Field", f.MetricCode), ("Reviewer", f.Reviewer));
            Row("32_FINANCIAL", f.Id, ("Mapping_ID", "MAP-" + f.Id), ("Source_ID", f.DocumentVersionId), ("Source_Location", f.Locator), ("Metric_Code", f.MetricCode), ("Property_ID", scope), ("Fiscal_Year", f.Period), ("Review_Status", "Verified"));
        }
        // Only complete, unique component pairs can supply a missing control. A stated total always takes precedence.
        foreach (var group in a.Facts.GroupBy(f => (f.Scope, f.Year, f.Scenario)).ToArray())
        foreach (var (total, first, second) in new[] { ("OPEX", "OPEX_SOCIETY", "OPEX_BUILDINGS"), ("DEBT_SERVICE", "DEBT_PRINCIPAL", "DEBT_INTEREST") })
        {
            var controls = group.Where(f => f.Code == total).ToArray(); var left = group.Where(f => f.Code == first).ToArray(); var right = group.Where(f => f.Code == second).ToArray();
            var pair = left.Length == 1 && right.Length == 1 && left[0].ValueState == "Available" && right[0].ValueState == "Available";
            if (controls.Length == 0 && pair)
            {
                var l = left[0]; var r = right[0]; var derivedId = "PAIR-" + l.Id + "-" + r.Id;
                a.Facts.Add(new(derivedId, l.Scope, l.Year, l.Scenario, total, l.Value + r.Value, "Available", l.Grain, "Verified", "Confirmed",
                    l.Certainty == "Placeholder" || r.Certainty == "Placeholder" ? "Placeholder" : "Verified source", input.Currency, l.SourceId, $"{l.SourceId}:{l.SourceLocation} + {r.SourceId}:{r.SourceLocation}", l.Cell,
                    first + " + " + second, [l.Id, r.Id]));
            }
            else if (controls.Length == 1 && controls[0].ValueState == "Available" && pair && Math.Abs(controls[0].Value!.Value - left[0].Value!.Value - right[0].Value!.Value) > .01m)
            {
                var control = controls[0]; a.Facts[a.Facts.IndexOf(control)] = control with { Value = null, ValueState = "Conflict" };
                Issue($"{control.Scope} / {control.Year}: {total} does not reconcile to {first} + {second}. The total is withheld pending review.");
            }
        }
        foreach (var warning in warnings) a.ImportFindings.Add(warning);
        if (input.FullOperationYear.Length == 0) a.ImportFindings.Add("Full-operation fiscal year is unconfirmed. Full-operation comparisons remain unavailable until an assessment specifies that period.");
        if (!input.TargetCoverage.HasValue) a.ImportFindings.Add("Target coverage is unconfirmed. Coverage ratio is calculated, but target-based gaps and risk thresholds remain unavailable.");
        V4CalculationEngine.Calculate(a);
        foreach (var year in years)
        foreach (var code in new[] { "REVENUE", "OPEX", "DEBT_SERVICE", "RESERVE_CLOSING", "HOMES_TOTAL" })
            if (a.Metric(portfolio, year, code)?.Value is null) Issue($"{year}: portfolio {code} is missing or conflicting. Supply a reviewed portfolio control; property rows are not automatically summed.");
        foreach (var warning in warnings.Where(w => w.Contains("competing totals"))) Issue(warning);
        a.Events.Add(new(Guid.NewGuid().ToString("N"), "Reviewed evidence assessment created", a.Id, 1, a.CreatedUtc, "Portal analyst", input.Reason, "", a.Sha256));
        a.Baseline = new("BASE-" + id, a.AssessmentId, tenant.Id, a.CreatedUtc, input.CurrentYear, portfolio, a.MethodologyVersion, a.Metrics.ToArray());
        return a;
    }
}
