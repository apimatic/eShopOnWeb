using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Firecrawl;

/// <summary>
/// A typed HttpClient over the Firecrawl v2 API. It reads a supplier's product listing with a
/// single <c>POST /v2/scrape</c> call that requests a schema-driven <c>json</c> format, so Firecrawl
/// returns the listing's products as structured data rather than raw markup.
/// </summary>
/// <remarks>
/// Contract verified against the official Firecrawl v2 documentation
/// (https://docs.firecrawl.dev/api-reference/endpoint/scrape and .../features/scrape):
/// the <c>json</c> format object carries a <c>prompt</c> and a JSON <c>schema</c>, and the extracted
/// object is returned at <c>data.json</c>.
/// </remarks>
public class FirecrawlClient : IFirecrawlClient
{
    private const string DefaultBaseUrl = "https://api.firecrawl.dev";
    private const string ScrapePath = "/v2/scrape";
    private const int ScrapeTimeoutMs = 120_000;

    private const string ExtractionPrompt =
        "This is a supplier's product listing page. Extract every distinct product shown on it. " +
        "For each product capture: its name; a description; the price as a plain number (no currency " +
        "symbol or thousands separators); the brand or manufacturer; the supplier's own product " +
        "identifier or SKU if one is shown; and the absolute URL of that product's own page if linked. " +
        "Return all products in the 'products' array. Do not invent products that are not on the page.";

    private readonly HttpClient _httpClient;
    private readonly FirecrawlOptions _options;
    private readonly ILogger<FirecrawlClient> _logger;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public FirecrawlClient(HttpClient httpClient, IOptions<FirecrawlOptions> options, ILogger<FirecrawlClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ScrapedProduct>> ScrapeProductListingAsync(string listingUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new FirecrawlException(
                "Firecrawl API key is not configured. Set 'Firecrawl:ApiKey' (from the FIRECRAWL_API_KEY environment variable).");
        }

        var requestUri = BuildRequestUri();
        var payload = BuildScrapeRequest(listingUrl);

        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is not FirecrawlException)
        {
            throw new FirecrawlException($"Firecrawl request to '{requestUri}' failed: {ex.Message}", ex);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new FirecrawlException(
                $"Firecrawl returned HTTP {(int)response.StatusCode} ({response.StatusCode}): {Snippet(body)}");
        }

        return ParseProducts(body);
    }

    private Uri BuildRequestUri()
    {
        var baseUrl = string.IsNullOrWhiteSpace(_options.BaseUrl) ? DefaultBaseUrl : _options.BaseUrl.Trim();
        return new Uri(baseUrl.TrimEnd('/') + ScrapePath);
    }

    private static string BuildScrapeRequest(string listingUrl)
    {
        var body = new
        {
            url = listingUrl,
            onlyMainContent = true,
            timeout = ScrapeTimeoutMs,
            formats = new object[]
            {
                new
                {
                    type = "json",
                    prompt = ExtractionPrompt,
                    schema = new
                    {
                        type = "object",
                        properties = new
                        {
                            products = new
                            {
                                type = "array",
                                items = new
                                {
                                    type = "object",
                                    properties = new
                                    {
                                        name = new { type = "string" },
                                        description = new { type = "string" },
                                        price = new { type = "number" },
                                        brand = new { type = "string" },
                                        sku = new { type = "string" },
                                        productUrl = new { type = "string" }
                                    },
                                    required = new[] { "name" }
                                }
                            }
                        },
                        required = new[] { "products" }
                    }
                }
            }
        };

        return JsonSerializer.Serialize(body, SerializerOptions);
    }

    private IReadOnlyList<ScrapedProduct> ParseProducts(string body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        if (root.TryGetProperty("success", out var success)
            && success.ValueKind == JsonValueKind.False)
        {
            var error = root.TryGetProperty("error", out var errorElement) ? errorElement.GetString() : null;
            throw new FirecrawlException($"Firecrawl reported failure: {error ?? Snippet(body)}");
        }

        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            throw new FirecrawlException($"Firecrawl response did not contain a 'data' object: {Snippet(body)}");
        }

        // The schema-driven extraction is returned at data.json for the scrape endpoint.
        if (!data.TryGetProperty("json", out var json) || json.ValueKind != JsonValueKind.Object)
        {
            _logger.LogWarning("Firecrawl returned no structured 'json' payload; treating the listing as empty.");
            return Array.Empty<ScrapedProduct>();
        }

        if (!json.TryGetProperty("products", out var products) || products.ValueKind != JsonValueKind.Array)
        {
            _logger.LogWarning("Firecrawl 'json' payload contained no 'products' array; treating the listing as empty.");
            return Array.Empty<ScrapedProduct>();
        }

        var result = new List<ScrapedProduct>();
        foreach (var element in products.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            result.Add(new ScrapedProduct
            {
                Name = GetString(element, "name"),
                Description = GetString(element, "description"),
                Price = GetPrice(element, "price"),
                Brand = GetString(element, "brand"),
                Sku = GetString(element, "sku"),
                ProductUrl = GetString(element, "productUrl")
            });
        }

        _logger.LogInformation("Firecrawl returned {Count} product(s) from the listing.", result.Count);
        return result;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private static decimal? GetPrice(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        switch (value.ValueKind)
        {
            case JsonValueKind.Number:
                return value.TryGetDecimal(out var number) ? number : null;
            case JsonValueKind.String:
                return ParsePriceString(value.GetString());
            default:
                return null;
        }
    }

    private static decimal? ParsePriceString(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        // Strip currency symbols, thousands separators and any surrounding text; keep digits,
        // the decimal point and a leading sign.
        var builder = new StringBuilder(raw.Length);
        foreach (var ch in raw)
        {
            if (char.IsDigit(ch) || ch == '.' || (ch == '-' && builder.Length == 0))
            {
                builder.Append(ch);
            }
        }

        return decimal.TryParse(builder.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var price)
            ? price
            : null;
    }

    private static string Snippet(string body)
    {
        if (string.IsNullOrEmpty(body))
        {
            return "<empty response>";
        }
        const int max = 500;
        return body.Length <= max ? body : body.Substring(0, max) + "…";
    }
}
