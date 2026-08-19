using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Reads a supplier's product listing using Firecrawl's synchronous structured scrape.
/// Contract (confirmed against docs.firecrawl.dev): POST {base}/v2/scrape with a "json" format that
/// carries a JSON schema; the extracted object is returned at data.json.
/// </summary>
public class FirecrawlClient : IFirecrawlClient
{
    private const string ScrapePath = "v2/scrape";

    private readonly HttpClient _httpClient;
    private readonly FirecrawlSettings _settings;
    private readonly IAppLogger<FirecrawlClient> _logger;

    public FirecrawlClient(HttpClient httpClient, IOptions<FirecrawlSettings> settings, IAppLogger<FirecrawlClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ScrapedProduct>> ScrapeProductsAsync(string listingUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            throw new FirecrawlException("Firecrawl API key is not configured (Firecrawl:ApiKey).");
        }

        var baseUrl = (string.IsNullOrWhiteSpace(_settings.BaseUrl) ? FirecrawlSettings.DefaultBaseUrl : _settings.BaseUrl!).TrimEnd('/');
        var requestUri = $"{baseUrl}/{ScrapePath}";

        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiKey);
        request.Content = new StringContent(BuildRequestBody(listingUrl), Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new FirecrawlException($"Firecrawl request to {requestUri} failed: {ex.Message}", ex);
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new FirecrawlException($"Firecrawl returned {(int)response.StatusCode} {response.ReasonPhrase}: {Trim(payload)}");
        }

        _logger.LogInformation($"Firecrawl scrape of {listingUrl} succeeded ({payload.Length} bytes).");
        return ParseProducts(payload);
    }

    private static string BuildRequestBody(string listingUrl)
    {
        var itemSchema = new
        {
            type = "object",
            properties = new
            {
                name = new { type = "string", description = "The product's display name." },
                description = new { type = "string", description = "A short product description." },
                price = new { type = "string", description = "The product's price exactly as shown, including currency." },
                brand = new { type = "string", description = "The product's brand or manufacturer." },
                sku = new { type = "string", description = "The supplier's SKU or product code, if shown." },
                productUrl = new { type = "string", description = "A link to the product's own page, if present." }
            },
            required = new[] { "name" }
        };

        var body = new
        {
            url = listingUrl,
            formats = new object[]
            {
                new
                {
                    type = "json",
                    prompt = "Extract every product listed on this page. For each product capture its name, "
                           + "description, price (as shown, including currency), brand, SKU/product code, and the URL "
                           + "of its own page if present. Return them all in the 'products' array.",
                    schema = new
                    {
                        type = "object",
                        properties = new { products = new { type = "array", items = itemSchema } },
                        required = new[] { "products" }
                    }
                }
            }
        };

        return JsonSerializer.Serialize(body);
    }

    private IReadOnlyList<ScrapedProduct> ParseProducts(string payload)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(payload);
        }
        catch (JsonException ex)
        {
            throw new FirecrawlException($"Firecrawl response was not valid JSON: {ex.Message}", ex);
        }

        using (document)
        {
            var root = document.RootElement;

            if (root.TryGetProperty("success", out var success) &&
                success.ValueKind == JsonValueKind.False)
            {
                var error = root.TryGetProperty("error", out var e) ? e.GetString() : "unknown error";
                throw new FirecrawlException($"Firecrawl reported failure: {error}");
            }

            if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object ||
                !data.TryGetProperty("json", out var json) || json.ValueKind != JsonValueKind.Object)
            {
                throw new FirecrawlException("Firecrawl response did not contain extracted data at data.json.");
            }

            if (!json.TryGetProperty("products", out var products) || products.ValueKind != JsonValueKind.Array)
            {
                // A well-formed response with no products means an empty listing, not an error.
                return Array.Empty<ScrapedProduct>();
            }

            var results = new List<ScrapedProduct>(products.GetArrayLength());
            foreach (var element in products.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object) continue;
                results.Add(new ScrapedProduct
                {
                    Name = ReadText(element, "name"),
                    Description = ReadText(element, "description"),
                    Price = ReadText(element, "price"),
                    Brand = ReadText(element, "brand"),
                    Sku = ReadText(element, "sku"),
                    ProductUrl = ReadText(element, "productUrl") ?? ReadText(element, "url")
                });
            }

            return results;
        }
    }

    /// <summary>Reads a property as text, tolerating string, number or boolean JSON values.</summary>
    private static string? ReadText(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
            _ => null
        };
    }

    private static string Trim(string value) => value.Length <= 500 ? value : value.Substring(0, 500);
}
