using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace IreiMvp.Web;

public sealed class SubmissionStore
{
    private readonly IreiOptions _options;
    private readonly IWebHostEnvironment _environment;
    private readonly TemplateTransformer _transformer;
    private readonly ILogger<SubmissionStore> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public SubmissionStore(
        IOptions<IreiOptions> options,
        IWebHostEnvironment environment,
        TemplateTransformer transformer,
        ILogger<SubmissionStore> logger)
    {
        _options = options.Value;
        _environment = environment;
        _transformer = transformer;
        _logger = logger;
    }

    public async Task<SubmissionRecord> CreateAsync(
        Stream input,
        string originalFileName,
        string organizationName,
        CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(originalFileName);
        if (!string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Only .xlsx workbooks are supported in this MVP.");
        }

        var id = $"{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}";
        var folder = Path.Combine(GetSubmissionsRoot(), id);
        Directory.CreateDirectory(folder);

        var inputPath = Path.Combine(folder, "input.xlsx");
        var outputPath = Path.Combine(folder, "admin-output.xlsx");
        var recordPath = Path.Combine(folder, "submission.json");

        await using (var file = File.Create(inputPath))
        {
            await input.CopyToAsync(file, cancellationToken);
        }

        ValidateWorkbookPackage(inputPath);

        var record = new SubmissionRecord
        {
            Id = id,
            OrganizationName = organizationName,
            OriginalFileName = Path.GetFileName(originalFileName),
            Status = "Processing",
            CreatedUtc = DateTimeOffset.UtcNow,
            InputPath = inputPath,
            OutputPath = outputPath
        };

        await SaveRecordAsync(recordPath, record, cancellationToken);

        try
        {
            var result = await _transformer.TransformAsync(
                inputPath,
                outputPath,
                organizationName,
                record.OriginalFileName,
                cancellationToken);

            record.Status = "Completed";
            record.CompletedUtc = DateTimeOffset.UtcNow;
            record.ProfileName = result.ProfileName;
            record.Dashboard = result.Dashboard;
            record.Warnings = result.Warnings;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Submission {SubmissionId} failed.", id);
            record.Status = "Failed";
            record.CompletedUtc = DateTimeOffset.UtcNow;
            record.Warnings.Add(exception.Message);
        }

        await SaveRecordAsync(recordPath, record, cancellationToken);
        return record;
    }

    public async Task<SubmissionRecord?> GetAsync(
        string id,
        CancellationToken cancellationToken)
    {
        if (!IsSafeIdentifier(id))
        {
            return null;
        }

        var path = Path.Combine(GetSubmissionsRoot(), id, "submission.json");
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<SubmissionRecord>(
            stream,
            _jsonOptions,
            cancellationToken);
    }

    public async Task<List<SubmissionRecord>> ListAsync(
        CancellationToken cancellationToken)
    {
        var root = GetSubmissionsRoot();
        Directory.CreateDirectory(root);

        var records = new List<SubmissionRecord>();
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            var path = Path.Combine(directory, "submission.json");
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                await using var stream = File.OpenRead(path);
                var record = await JsonSerializer.DeserializeAsync<SubmissionRecord>(
                    stream,
                    _jsonOptions,
                    cancellationToken);

                if (record is not null)
                {
                    records.Add(record);
                }
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Could not read submission metadata from {Path}.",
                    path);
            }
        }

        return records
            .OrderByDescending(record => record.CreatedUtc)
            .ToList();
    }

    public string? GetOutputPath(SubmissionRecord record) =>
        File.Exists(record.OutputPath) ? record.OutputPath : null;

    private async Task SaveRecordAsync(
        string path,
        SubmissionRecord record,
        CancellationToken cancellationToken)
    {
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(
            stream,
            record,
            _jsonOptions,
            cancellationToken);
    }

    private void ValidateWorkbookPackage(string path)
    {
        var info = new FileInfo(path);
        if (info.Length == 0 || info.Length > _options.MaximumUploadBytes)
        {
            throw new InvalidDataException(
                $"Workbook size must be between 1 byte and {_options.MaximumUploadBytes:N0} bytes.");
        }

        try
        {
            using var archive = ZipFile.OpenRead(path);
            if (archive.GetEntry("xl/workbook.xml") is null ||
                archive.GetEntry("[Content_Types].xml") is null)
            {
                throw new InvalidDataException(
                    "The uploaded file is not a valid Office Open XML workbook.");
            }
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new InvalidDataException(
                "The uploaded file is not a readable .xlsx package.",
                exception);
        }
    }

    private string GetSubmissionsRoot()
    {
        var root = ResolvePath(_options.DataRoot);
        return Path.Combine(root, "Submissions");
    }

    private string ResolvePath(string configuredPath) =>
        Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(_environment.ContentRootPath, configuredPath);

    private static bool IsSafeIdentifier(string value) =>
        value.Length is > 5 and < 100 &&
        value.All(character => char.IsLetterOrDigit(character) || character == '-');
}
