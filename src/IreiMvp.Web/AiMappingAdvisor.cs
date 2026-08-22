using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace IreiMvp.Web;

public sealed class AiMappingAdvisor
{
    private readonly HttpClient _httpClient;
    private readonly IreiOptions _options;
    private readonly ILogger<AiMappingAdvisor> _logger;

    public AiMappingAdvisor(
        HttpClient httpClient,
        IOptions<IreiOptions> options,
        ILogger<AiMappingAdvisor> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MappingSuggestion>> SuggestAsync(
        WorkbookSnapshot source,
        WorkbookSnapshot template,
        CancellationToken cancellationToken)
    {
        if (!_options.Ai.Enabled)
        {
            return Array.Empty<MappingSuggestion>();
        }

        var apiKey = string.IsNullOrWhiteSpace(_options.Ai.ApiKey)
            ? Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            : _options.Ai.ApiKey;

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogInformation(
                "OPENAI_API_KEY is not configured. Local pattern mapping will continue.");
            return Array.Empty<MappingSuggestion>();
        }

        var sourceProfiles = source.Sheets.Values
            .Select(ProfileSheet)
            .Where(profile => profile.Headers.Count > 0)
            .Take(30)
            .ToList();

        var targetProfiles = template.Sheets.Values
            .Select(ProfileSheet)
            .Where(profile => profile.Headers.Count >= 3)
            .Take(20)
            .ToList();

        var systemPrompt = """
You map arbitrary client Excel tables into a runtime-discovered IREI target workbook.
Source sheet names, header rows, column order and terminology change between clients.
Never assume a fixed source tab name or fixed source column.
Return only high-confidence source-field to target-field suggestions.
Do not invent values and do not map into formula fields.
""";

        var userPayload = new
        {
            sourceSheets = sourceProfiles.Select(profile => new
            {
                profile.Name,
                profile.HeaderRow,
                profile.Headers,
                profile.SampleRows
            }),
            targetTables = targetProfiles.Select(profile => new
            {
                sheet = profile.Name,
                profile.HeaderRow,
                headers = profile.Headers
            })
        };

        var body = new
        {
            model = _options.Ai.Model,
            store = false,
            max_output_tokens = _options.Ai.MaxOutputTokens,
            input = new object[]
            {
                new { role = "system", content = systemPrompt },
                new
                {
                    role = "user",
                    content = JsonSerializer.Serialize(userPayload, JsonOptions)
                }
            },
            text = new
            {
                format = new
                {
                    type = "json_schema",
                    name = "irei_field_mapping_suggestions",
                    strict = true,
                    schema = BuildSchema()
                }
            }
        };

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                _options.Ai.Endpoint)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(body, JsonOptions),
                    Encoding.UTF8,
                    "application/json")
            };
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", apiKey);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "OpenAI mapping returned HTTP {StatusCode}. Local pattern mapping will continue. Response: {Response}",
                    (int)response.StatusCode,
                    responseText[..Math.Min(responseText.Length, 500)]);
                return Array.Empty<MappingSuggestion>();
            }

            using var responseDocument = JsonDocument.Parse(responseText);
            var outputText = ExtractOutputText(responseDocument.RootElement);
            if (string.IsNullOrWhiteSpace(outputText))
            {
                return Array.Empty<MappingSuggestion>();
            }

            var result = JsonSerializer.Deserialize<AiMappingResponse>(
                outputText,
                JsonOptions);

            var filtered = result?.Mappings?.Where(mapping =>
                    mapping.Confidence >= _options.Ai.MinimumAutoApplyConfidence)
                .ToList();

            return filtered is null
                ? Array.Empty<MappingSuggestion>()
                : filtered;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "AI mapping was unavailable. Local pattern mapping will continue.");
            return Array.Empty<MappingSuggestion>();
        }
    }

    private static SheetProfile ProfileSheet(SheetSnapshot sheet)
    {
        var headerRow = SmartMappingEngine.FindLikelyHeaderRow(sheet);
        var headers = new List<string>();

        if (headerRow > 0)
        {
            for (var column = 1; column <= Math.Min(sheet.MaxColumn, 35); column++)
            {
                headers.Add(sheet.GetValue(headerRow, column)?.ToString()?.Trim() ?? "");
            }
        }

        var samples = new List<List<object?>>();
        for (var row = headerRow + 1;
             row <= Math.Min(sheet.MaxRow, headerRow + 2);
             row++)
        {
            var values = new List<object?>();
            for (var column = 1; column <= Math.Min(sheet.MaxColumn, 20); column++)
            {
                values.Add(sheet.GetValue(row, column));
            }

            if (values.Any(value => value is not null &&
                                    !string.IsNullOrWhiteSpace(value.ToString())))
            {
                samples.Add(values);
            }
        }

        return new SheetProfile
        {
            Name = sheet.Name,
            HeaderRow = headerRow,
            Headers = headers,
            SampleRows = samples
        };
    }

    private static object BuildSchema()
    {
        return new
        {
            type = "object",
            additionalProperties = false,
            properties = new
            {
                mappings = new
                {
                    type = "array",
                    items = new
                    {
                        type = "object",
                        additionalProperties = false,
                        properties = new
                        {
                            targetSheet = new { type = "string" },
                            targetField = new { type = "string" },
                            sourceSheet = new { type = "string" },
                            sourceField = new { type = "string" },
                            confidence = new { type = "number" },
                            reason = new { type = "string" }
                        },
                        required = new[]
                        {
                            "targetSheet", "targetField", "sourceSheet",
                            "sourceField", "confidence", "reason"
                        }
                    }
                }
            },
            required = new[] { "mappings" }
        };
    }

    private static string? ExtractOutputText(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("type", out var type) &&
                type.ValueKind == JsonValueKind.String &&
                string.Equals(type.GetString(), "output_text", StringComparison.OrdinalIgnoreCase) &&
                element.TryGetProperty("text", out var text) &&
                text.ValueKind == JsonValueKind.String)
            {
                return text.GetString();
            }

            foreach (var property in element.EnumerateObject())
            {
                var nested = ExtractOutputText(property.Value);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = ExtractOutputText(item);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
