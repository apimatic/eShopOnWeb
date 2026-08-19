using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Firecrawl;

/// <summary>
/// Reads a supplier's product listing through the Firecrawl v2 <c>/scrape</c> endpoint, asking it
/// to return the products on the page as structured JSON matching a fixed schema.
/// Contract verified against the official Firecrawl API reference (https://docs.firecrawl.dev):
/// POST {baseUrl}/v2/scrape, Bearer auth, body { url, formats:[{ type:"json", schema, prompt }] },
/// response { success, data: { json, warning } }.
/// </summary>
public class FirecrawlClient : IFirecrawlClient
{
    private const string ScrapePath = "/v2/scrape";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // JSON schema handed to Firecrawl describing the products to extract from the listing page.
    private static readonly object ProductsSchema = new
    {
        type = "object",
        properties = new
        {
            products = new
            {
                type = "array",
                description = "Every distinct product offered on the page.",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        name = new { type = "string", description = "Product name / title." },
                        description = new { type = "string", description = "Short product description." },
                        price = new { type = "number", description = "Numeric price of the product, without currency symbols." },
                        brand = new { type = "string", description = "Brand or manufacturer, if shown." },
                        url = new { type = "string", description = "Absolute URL of the product's own page/link." },
                        externalId = new { type = "string", description = "The supplier's own product id or SKU, if shown." }
                    },
                    required = new[] { "name", "price" }
                }
            }
        },
        required = new[] { "products" }
    };

    private const string ExtractionPrompt =
        "Extract every product listed on this supplier product page. For each product capture its " +
        "name, a short description, its numeric price (no currency symbols), its brand or manufacturer, " +
        "the absolute URL of the product's own page, and the supplier's own product id/SKU if present. " +
        "Only include real products offered for sale.";

    private readonly HttpClient _httpClient;
    private readonly FirecrawlConfiguration _configuration;
    private readonly IAppLogger<FirecrawlClient> _logger;

    public FirecrawlClient(HttpClient httpClient, IOptions<FirecrawlConfiguration> configuration, IAppLogger<FirecrawlClient> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration.Value;
        _logger = logger;
    }

    public async Task<FirecrawlScrapeResult> ScrapeProductListingAsync(string listingUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_configuration.ApiKey))
        {
            return FirecrawlScrapeResult.Fail("Firecrawl API key is not configured (Firecrawl:ApiKey).");
        }

        var requestUri = $"{_configuration.EffectiveBaseUrl.TrimEnd('/')}{ScrapePath}";
        var payload = new
        {
            url = listingUrl,
            onlyMainContent = true,
            formats = new object[]
            {
                new { type = "json", prompt = ExtractionPrompt, schema = ProductsSchema }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = JsonContent.Create(payload, options: SerializerOptions)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _configuration.ApiKey);

        _logger.LogInformation("Firecrawl scrape requested for {0} via {1}.", listingUrl, requestUri);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return FirecrawlScrapeResult.Fail(
                $"Firecrawl returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}): {Truncate(body, 500)}");
        }

        try
        {
            return ParseResponse(body);
        }
        catch (JsonException ex)
        {
            return FirecrawlScrapeResult.Fail($"Could not parse Firecrawl response: {ex.Message}");
        }
    }

    private static FirecrawlScrapeResult ParseResponse(string body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        if (root.TryGetProperty("success", out var successElement) &&
            successElement.ValueKind == JsonValueKind.False)
        {
            var error = root.TryGetProperty("error", out var errorElement)
                ? errorElement.GetString()
                : "Firecrawl reported an unsuccessful scrape.";
            return FirecrawlScrapeResult.Fail(error ?? "Firecrawl reported an unsuccessful scrape.");
        }

        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            return FirecrawlScrapeResult.Fail("Firecrawl response did not contain a data object.");
        }

        string? warning = data.TryGetProperty("warning", out var warningElement) && warningElement.ValueKind == JsonValueKind.String
            ? warningElement.GetString()
            : null;

        var products = new List<ScrapedProduct>();
        if (data.TryGetProperty("json", out var json) &&
            json.ValueKind == JsonValueKind.Object &&
            json.TryGetProperty("products", out var productsElement) &&
            productsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in productsElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                products.Add(new ScrapedProduct
                {
                    Name = GetString(element, "name"),
                    Description = GetString(element, "description"),
                    Price = GetDecimal(element, "price"),
                    Brand = GetString(element, "brand"),
                    Url = GetString(element, "url"),
                    ExternalId = GetString(element, "externalId")
                });
            }
        }

        return FirecrawlScrapeResult.Ok(products, warning);
    }

    private static string? GetString(JsonElement parent, string propertyName)
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

    private static decimal? GetDecimal(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        switch (value.ValueKind)
        {
            case JsonValueKind.Number when value.TryGetDecimal(out var number):
                return number;
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

        // Keep digits, decimal point and sign; drop currency symbols, thousands separators, etc.
        var cleaned = new string(Array.FindAll(raw.ToCharArray(), c => char.IsDigit(c) || c == '.' || c == '-'));
        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static string Truncate(string value, int maxLength) =>
        string.IsNullOrEmpty(value) || value.Length <= maxLength ? value : value.Substring(0, maxLength) + "...";
}
