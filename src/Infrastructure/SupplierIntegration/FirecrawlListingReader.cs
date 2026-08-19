using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.SupplierIntegration;

/// <summary>
/// Reads a supplier's product listing page with Firecrawl's scrape endpoint, asking for
/// structured JSON extraction (an array of products) against a JSON Schema.
///
/// Contract verified against the official Firecrawl documentation
/// (https://docs.firecrawl.dev/api-reference/endpoint/scrape, "Scrape" feature docs):
///   POST {baseUrl}/v2/scrape
///   Authorization: Bearer &lt;apiKey&gt;
///   body: { "url", "formats": [ { "type": "json", "schema", "prompt" }, ... ] }
///   response: { "success", "data": { "json": &lt;matches schema&gt;, "metadata": { ... } }, "warning" }
/// A wrapper object with an array field is used so Firecrawl returns every product, not just the
/// first match.
/// </summary>
public class FirecrawlListingReader : ISupplierListingReader
{
    private const string DefaultBaseUrl = "https://api.firecrawl.dev";
    private const string ScrapePath = "/v2/scrape";
    private const int ScrapeTimeoutMs = 120_000;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly FirecrawlSettings _settings;
    private readonly IAppLogger<FirecrawlListingReader> _logger;

    public FirecrawlListingReader(
        HttpClient httpClient,
        IOptions<FirecrawlSettings> settings,
        IAppLogger<FirecrawlListingReader> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<SupplierListingResult> ReadListingAsync(string listingUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            throw new InvalidOperationException(
                "Firecrawl API key is not configured. Set 'Firecrawl:ApiKey' (from the FIRECRAWL_API_KEY environment variable / user-secrets).");
        }

        var requestUri = BuildScrapeUri();
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(BuildRequestBody(listingUrl), SerializerOptions),
            Encoding.UTF8, "application/json");

        _logger.LogInformation("Requesting Firecrawl scrape of '{0}' via {1}.", listingUrl, requestUri);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Firecrawl returned {(int)response.StatusCode} {response.ReasonPhrase}: {Trim(payload)}");
        }

        return ParseResponse(payload);
    }

    private Uri BuildScrapeUri()
    {
        var baseUrl = string.IsNullOrWhiteSpace(_settings.BaseUrl) ? DefaultBaseUrl : _settings.BaseUrl.Trim();
        return new Uri(baseUrl.TrimEnd('/') + ScrapePath);
    }

    private static object BuildRequestBody(string listingUrl)
    {
        var productProperties = new
        {
            name = new { type = "string", description = "The product's name or title." },
            description = new { type = "string", description = "The product's description text." },
            price = new { type = "number", description = "The product's price as a number, without currency symbols." },
            brand = new { type = "string", description = "The product's brand or manufacturer." },
            category = new { type = "string", description = "The product's category, if shown." },
            url = new { type = "string", description = "The absolute URL of the product's own page, if present." },
            sku = new { type = "string", description = "The supplier's own identifier, SKU or product code, if present." }
        };

        var schema = new
        {
            type = "object",
            properties = new
            {
                products = new
                {
                    type = "array",
                    description = "Every product shown in the supplier's product listing.",
                    items = new
                    {
                        type = "object",
                        properties = productProperties,
                        required = new[] { "name" }
                    }
                }
            },
            required = new[] { "products" }
        };

        return new
        {
            url = listingUrl,
            onlyMainContent = true,
            timeout = ScrapeTimeoutMs,
            formats = new object[]
            {
                new
                {
                    type = "json",
                    prompt = "Extract every product listed on this supplier's product listing page. " +
                             "For each product capture its name, full description, price (as a number), brand, " +
                             "category if shown, the absolute URL of the product's own page, and the supplier's " +
                             "own identifier/SKU if present. Include every product on the page.",
                    schema
                }
            }
        };
    }

    private SupplierListingResult ParseResponse(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        if (root.TryGetProperty("success", out var success) &&
            success.ValueKind == JsonValueKind.False)
        {
            var error = root.TryGetProperty("error", out var errorElement) ? errorElement.GetString() : null;
            throw new HttpRequestException($"Firecrawl reported failure: {error ?? "unknown error"}.");
        }

        var listingFullyCaptured = true;
        if (root.TryGetProperty("warning", out var warning) && warning.ValueKind == JsonValueKind.String)
        {
            var warningText = warning.GetString();
            if (!string.IsNullOrWhiteSpace(warningText))
            {
                _logger.LogWarning("Firecrawl returned a warning: {0}", warningText);
                listingFullyCaptured = !IndicatesTruncation(warningText);
            }
        }

        var products = new List<ScrapedProduct>();
        if (root.TryGetProperty("data", out var data) &&
            data.TryGetProperty("json", out var json) &&
            json.ValueKind == JsonValueKind.Object &&
            json.TryGetProperty("products", out var productArray) &&
            productArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in productArray.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                products.Add(new ScrapedProduct
                {
                    Name = ReadString(element, "name"),
                    Description = ReadString(element, "description"),
                    Price = ReadPrice(element, "price"),
                    Brand = ReadString(element, "brand"),
                    Category = ReadString(element, "category"),
                    Url = ReadString(element, "url"),
                    Sku = ReadString(element, "sku")
                });
            }
        }
        else
        {
            _logger.LogWarning("Firecrawl response contained no extracted products (data.json.products missing).");
        }

        return new SupplierListingResult(products, listingFullyCaptured);
    }

    private static bool IndicatesTruncation(string warningText)
    {
        var lowered = warningText.ToLowerInvariant();
        return lowered.Contains("truncat") || lowered.Contains("incomplete") ||
               lowered.Contains("token limit") || lowered.Contains("max tokens");
    }

    private static string? ReadString(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            _ => null
        };
    }

    private static decimal? ReadPrice(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            var raw = value.GetString();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            // Keep only digits, a decimal point and a leading sign so "£51.77", "$1,299.00" etc. parse.
            var cleaned = new string(raw.Where(c => char.IsDigit(c) || c == '.' || c == '-').ToArray());
            if (decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static string Trim(string value) =>
        value.Length <= 500 ? value : value.Substring(0, 500) + "...";
}
