using Microsoft.Extensions.Options;

namespace IreiMvp.Web;

public sealed class TemplateTransformer
{
    private readonly XlsxWorkbookService _xlsx;
    private readonly SmartMappingEngine _mappingEngine;
    private readonly DashboardMetricsFactory _metricsFactory;
    private readonly IreiOptions _options;
    private readonly IWebHostEnvironment _environment;

    public TemplateTransformer(
        XlsxWorkbookService xlsx,
        SmartMappingEngine mappingEngine,
        DashboardMetricsFactory metricsFactory,
        IOptions<IreiOptions> options,
        IWebHostEnvironment environment)
    {
        _xlsx = xlsx;
        _mappingEngine = mappingEngine;
        _metricsFactory = metricsFactory;
        _options = options.Value;
        _environment = environment;
    }

    public async Task<TransformResult> TransformAsync(
        string inputPath,
        string outputPath,
        string organizationName,
        string originalFileName,
        CancellationToken cancellationToken)
    {
        var templatePath = ResolvePath(_options.TemplatePath);
        var source = _xlsx.Read(inputPath);
        var template = _xlsx.Read(templatePath);

        var mapping = await _mappingEngine.BuildAsync(
            source,
            template,
            organizationName,
            originalFileName,
            cancellationToken);

        const string profileName =
            "Runtime workbook pattern mapping with optional AI field suggestions";

        var dashboard = _metricsFactory.Build(
            template,
            mapping.Patches,
            organizationName,
            profileName,
            mapping.Warnings);

        if (!string.IsNullOrWhiteSpace(mapping.ReportingPeriod))
        {
            dashboard.ReportingPeriod = mapping.ReportingPeriod;
        }

        _xlsx.CopyAndPatch(
            templatePath,
            outputPath,
            mapping.Patches);

        return new TransformResult
        {
            ProfileName = profileName,
            Dashboard = dashboard,
            Warnings = mapping.Warnings,
            PatchCount = mapping.Patches.Count
        };
    }

    private string ResolvePath(string configuredPath) =>
        Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(_environment.ContentRootPath, configuredPath);
}
