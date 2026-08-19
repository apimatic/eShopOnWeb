using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.CatalogSync;

/// <summary>
/// Reads a supplier's product listing with Firecrawl's v2 <c>/scrape</c> endpoint using the
/// synchronous structured-extraction (<c>json</c>) format: one call returns the products on the
/// page, shaped by the JSON schema below.
/// See https://docs.firecrawl.dev/features/scrape (verified 2026-08-19).
/// </summary>
public class FirecrawlListingReader : ISupplierListingReader
{
    private readonly HttpClient _httpClient;
    private readonly FirecrawlOptions _options;
    private readonly ILogger<FirecrawlListingReader> _logger;

    public FirecrawlListingReader(
        HttpClient httpClient,
        IOptions<FirecrawlOptions> options,
        ILogger<FirecrawlListingReader> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<SupplierListingResult> ReadListingAsync(string listingUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            return SupplierListingResult.Failed(
                "Firecrawl API key is not configured (set Firecrawl:ApiKey from FIRECRAWL_API_KEY).");
        }

        var requestUri = $"{_options.ResolveBaseUrl().TrimEnd('/')}/v2/scrape";
        var payload = BuildScrapeRequest(listingUrl);

        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        HttpResponseMessage response;
        string body;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogError(ex, "Firecrawl scrape request to {Url} failed.", listingUrl);
            return SupplierListingResult.Failed($"Firecrawl request failed: {ex.Message}");
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Firecrawl scrape of {Url} returned {Status}: {Body}", listingUrl, (int)response.StatusCode, Trim(body));
            return SupplierListingResult.Failed($"Firecrawl returned HTTP {(int)response.StatusCode}: {Trim(body)}");
        }

        try
        {
            return ParseProducts(body);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Could not parse Firecrawl response for {Url}.", listingUrl);
            return SupplierListingResult.Failed($"Could not parse Firecrawl response: {ex.Message}");
        }
    }

    /// <summary>
    /// Builds the v2 scrape request: a single <c>json</c> format entry carrying the extraction
    /// schema (a top-level object with a <c>products</c> array) plus a natural-language prompt.
    /// </summary>
    private static object BuildScrapeRequest(string listingUrl)
    {
        var productSchema = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object>
            {
                ["name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "The product's name or title." },
                ["description"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "The product's description." },
                ["price"] = new Dictionary<string, object> { ["type"] = "number", ["description"] = "The product's price as a number, without a currency symbol." },
                ["brand"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "The product's brand or manufacturer." },
                ["url"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "The absolute URL of the product's detail page, if present." },
                ["sku"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "The supplier's SKU or product code, if present." }
            },
            ["required"] = new[] { "name", "price" }
        };

        var schema = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object>
            {
                ["products"] = new Dictionary<string, object>
                {
                    ["type"] = "array",
                    ["items"] = productSchema
                }
            },
            ["required"] = new[] { "products" }
        };

        return new Dictionary<string, object>
        {
            ["url"] = listingUrl,
            ["onlyMainContent"] = false,
            ["formats"] = new object[]
            {
                new Dictionary<string, object>
                {
                    ["type"] = "json",
                    ["prompt"] = "Extract every product shown on this supplier's product listing page. " +
                                 "For each product capture its name, description, price (as a number), brand, " +
                                 "the URL of its detail page if present, and its SKU/product code if present.",
                    ["schema"] = schema
                }
            }
        };
    }

    /// <summary>
    /// Pulls the products out of <c>data.json.products</c>. Parsing is deliberately tolerant of
    /// messy scraped values (e.g. a price rendered as "$29.99").
    /// </summary>
    private SupplierListingResult ParseProducts(string body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        if (root.TryGetProperty("success", out var success) &&
            success.ValueKind == JsonValueKind.False)
        {
            var message = root.TryGetProperty("error", out var error) ? error.GetString() : null;
            return SupplierListingResult.Failed($"Firecrawl reported failure: {message ?? "unknown error"}.");
        }

        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            return SupplierListingResult.Ok(new List<SupplierProduct>());
        }

        if (!TryGetProductsArray(data, out var productsArray))
        {
            // The listing was read but held no recognizable products — a full (empty) capture.
            return SupplierListingResult.Ok(new List<SupplierProduct>());
        }

        var products = new List<SupplierProduct>();
        foreach (var element in productsArray.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            products.Add(new SupplierProduct
            {
                Name = GetString(element, "name"),
                Description = GetString(element, "description"),
                Brand = GetString(element, "brand"),
                Url = GetString(element, "url"),
                Sku = GetString(element, "sku"),
                Price = GetPrice(element, "price")
            });
        }

        return SupplierListingResult.Ok(products);
    }

    private static bool TryGetProductsArray(JsonElement data, out JsonElement productsArray)
    {
        // Expected shape: data.json.products. Fall back to data.extract.products in case the
        // response used the extract-style envelope.
        foreach (var container in new[] { "json", "extract" })
        {
            if (data.TryGetProperty(container, out var json) && json.ValueKind == JsonValueKind.Object &&
                json.TryGetProperty("products", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                productsArray = arr;
                return true;
            }
        }

        productsArray = default;
        return false;
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
            case JsonValueKind.Number when value.TryGetDecimal(out var number):
                return number;
            case JsonValueKind.String:
                return ParsePriceString(value.GetString());
            default:
                return null;
        }
    }

    /// <summary>Parses a price that arrived as a string such as "$1,299.00" or "€49,99".</summary>
    private static decimal? ParsePriceString(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var builder = new StringBuilder(raw.Length);
        foreach (var ch in raw)
        {
            if (char.IsDigit(ch) || ch == '.' || ch == ',' || ch == '-')
            {
                builder.Append(ch);
            }
        }

        var cleaned = builder.ToString();
        if (cleaned.Length == 0)
        {
            return null;
        }

        // Treat a comma as a thousands separator when a decimal point is also present, otherwise
        // as a decimal separator (European style).
        if (cleaned.Contains('.') && cleaned.Contains(','))
        {
            cleaned = cleaned.Replace(",", string.Empty);
        }
        else if (cleaned.Contains(','))
        {
            cleaned = cleaned.Replace(',', '.');
        }

        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var price)
            ? price
            : null;
    }

    private static string Trim(string value) =>
        value.Length <= 500 ? value : value.Substring(0, 500);
}
