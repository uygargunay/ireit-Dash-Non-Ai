using System.Globalization;
using System.Net;
using System.Text;

namespace IreiMvp.Web;

public static class EvidenceAssessmentReport
{
    public static string Render(V4Assessment a)
    {
        static string E(object? value) => WebUtility.HtmlEncode(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "");
        static string Value(V4Metric? m) => m?.Value is not decimal v ? E(m?.ValueState == "Conflict" ? "Conflict — review needed" : m?.ValueState == "Not Applicable" ? "Not applicable" : "Missing evidence") :
            E(v.ToString(m.Code == "HOMES_TOTAL" ? "N0" : m.Code.Contains("DSCR") ? "0.000'×'" : "N2", CultureInfo.InvariantCulture));
        var portfolio = a.Setting("PORTFOLIO_PROPERTY_ID"); var year = a.Setting("CURRENT_FISCAL_YEAR");
        var columns = new[] { ("REVENUE", "Revenue"), ("OPEX", "Operating costs"), ("NOI", "Income after costs"), ("DEBT_SERVICE", "Loan payments"), ("DSCR", "Debt coverage"), ("CASH_AFTER_DEBT", "Cash after loans"), ("RESERVE_CLOSING", "Closing reserves"), ("HOMES_TOTAL", "Homes / beds") };
        var b = new StringBuilder("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>IREI — draft assessment</title><link rel=\"stylesheet\" href=\"/evidence-report.css\"><script src=\"/evidence-report.js\" defer></script></head><body><main>");
        b.Append("<div class=\"tools\"><button id=\"print-report\">Print / save as PDF</button><a href=\"/evidence.html\">Back to evidence</a></div><p class=\"badge\">INTERNAL DRAFT — NOT APPROVED FOR EXTERNAL RELEASE</p><h1>").Append(E(a.Organization)).Append("</h1><h2>Financial assessment</h2><p>Current fiscal year: ").Append(E(year)).Append(" · ").Append(E(a.Currency)).Append(" · Working version ").Append(a.Version).Append(" · Created ").Append(E(a.CreatedUtc.ToString("yyyy-MM-dd"))).Append("</p><p>Calculated from selected, reviewed source records. Missing evidence is shown explicitly and is never treated as zero. This draft is separate from client factual sign-off and report release.</p>");
        b.Append("<h2>Portfolio financial position</h2><p>Money is shown in ").Append(E(a.Currency)).Append(" dollars. Coverage is a ratio. Reserves are not automatically available to spend or borrow against.</p><div class=\"table-scroll\"><table><thead><tr><th>Fiscal year / basis</th>");
        foreach (var (_, label) in columns) b.Append("<th>").Append(E(label)).Append("</th>");
        b.Append("</tr></thead><tbody>");
        foreach (var (period, scenario) in V4CalculationEngine.Years(a))
        {
            b.Append("<tr><th>").Append(E(period)).Append("<small>").Append(E(scenario)).Append("</small></th>");
            foreach (var (code, _) in columns) b.Append("<td>").Append(Value(a.Metric(portfolio, period, code))).Append("</td>");
            b.Append("</tr>");
        }
        b.Append("</tbody></table></div><h2>What this tells us</h2><ul>");
        var noi = a.Metric(portfolio, year, "NOI"); var cash = a.Metric(portfolio, year, "CASH_AFTER_DEBT"); var coverage = a.Metric(portfolio, year, "DSCR");
        b.Append("<li>Income after operating costs: ").Append(Value(noi)).Append(".</li><li>Cash after annual loan payments, before reserve contributions and other commitments: ").Append(Value(cash)).Append(".</li><li>Annual debt coverage: ").Append(Value(coverage)).Append(".</li>");
        if (cash?.Value < 0) b.Append("<li>A cash shortfall is indicated for the selected year. Funding, payment terms and permitted uses require further review.</li>");
        else if (cash?.Value >= 0) b.Append("<li>Nonnegative cash after loans does not establish unrestricted cash, distributable surplus or safe borrowing capacity.</li>");
        b.Append("<li>Property values, financing permissions, liquidity obligations and lender terms are required before a borrowing recommendation.</li></ul><h2>Property detail — ").Append(E(year)).Append("</h2>");
        var properties = a.Rows("31_PROPERTY").Where(p => p.Text("Include_in_Assessment") == "Yes").ToArray();
        if (properties.Length == 0) b.Append("<p>No property detail has been confirmed in this assessment. Portfolio totals are not inferred from a partial property sample.</p>");
        else
        {
            b.Append("<table><thead><tr><th>Property</th><th>Income after costs</th><th>Loan payments</th><th>Coverage</th></tr></thead><tbody>");
            foreach (var p in properties) b.Append("<tr><th>").Append(E(p.Text("Property_Name"))).Append("</th><td>").Append(Value(a.Metric(p.Id, year, "NOI"))).Append("</td><td>").Append(Value(a.Metric(p.Id, year, "DEBT_SERVICE"))).Append("</td><td>").Append(Value(a.Metric(p.Id, year, "DSCR"))).Append("</td></tr>");
            b.Append("</tbody></table>");
        }
        b.Append("<h2>Missing information and questions</h2><ul>");
        foreach (var finding in a.ImportFindings.Concat(a.Rows("51_EXCEPTIONS").Where(r => r.Text("Status") == "Open").Select(r => r.Text("Description"))).Distinct()) b.Append("<li>").Append(E(finding)).Append("</li>");
        b.Append("</ul><h2>Source evidence</h2><p>Results retain their original file versions and source locations. Use the dashboard evidence buttons for fact-level calculation traces.</p><table><thead><tr><th>Source file</th><th>Version</th><th>Identity (SHA-256)</th></tr></thead><tbody>");
        foreach (var source in a.Rows("10_SOURCE_REGISTER")) b.Append("<tr><td>").Append(E(source.Text("Source_Name"))).Append("</td><td>").Append(E(source.Text("Version"))).Append("</td><td class=\"hash\">").Append(E(source.Text("SHA256"))).Append("</td></tr>");
        b.Append("</tbody></table><p>Method: ").Append(E(a.MethodologyVersion)).Append(" · Assessment: ").Append(E(a.AssessmentId)).Append("</p></main></body></html>");
        return b.ToString();
    }
}
