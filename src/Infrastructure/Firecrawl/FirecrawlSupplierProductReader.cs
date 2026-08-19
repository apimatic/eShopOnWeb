using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FirecrawlApi;
using FirecrawlApi.Core.Exceptions;
using FirecrawlApi.Core.ErrorResponse;
using FirecrawlApi.Errors;
using FirecrawlApi.Models;
using FirecrawlApi.Models.Enums;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Firecrawl;

/// <summary>
/// Reads a supplier's product-listing page with Firecrawl's LLM extraction. It submits an extract
/// job with a product schema, polls it to completion, and maps the structured result to
/// <see cref="SupplierProduct"/>s. All Firecrawl access goes through the vendored Firecrawl .NET SDK.
/// </summary>
public class FirecrawlSupplierProductReader : ISupplierProductReader
{
    private const string ExtractionPrompt =
        "Extract every product listed on this supplier's product-listing page. " +
        "For each product capture: its name, its description, its price as a number, its brand, " +
        "the URL of the product's own page, and the product image URL. " +
        "Return all products found on the listing, following the provided schema exactly.";

    private readonly FirecrawlApiClient _client;
    private readonly FirecrawlOptions _options;

    public FirecrawlSupplierProductReader(FirecrawlApiClient client, IOptions<FirecrawlOptions> options)
    {
        _client = client;
        _options = options.Value;
    }

    public async Task<SupplierProductReadResult> ReadProductsAsync(string listingUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(listingUrl))
        {
            throw new ArgumentException("A supplier listing URL is required.", nameof(listingUrl));
        }

        var request = new ExtractRequest
        {
            Urls = new[] { listingUrl },
            Prompt = ExtractionPrompt,
            Schema = BuildProductSchema()
        };

        ExtractResponse start;
        try
        {
            start = await _client.Extraction.ExtractData(request, ct: cancellationToken);
        }
        catch (SdkException<ExtractDataError> ex)
        {
            throw new InvalidOperationException($"Firecrawl rejected the extract request: {Describe(ex.Error)}", ex);
        }

        if (string.IsNullOrWhiteSpace(start.Id))
        {
            throw new InvalidOperationException("Firecrawl did not return an extract job id.");
        }

        if (!Guid.TryParse(start.Id, out var jobId))
        {
            throw new InvalidOperationException($"Firecrawl returned an unparseable extract job id '{start.Id}'.");
        }

        var pollDelay = TimeSpan.FromSeconds(Math.Max(1, _options.PollIntervalSeconds));
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(Math.Max(pollDelay.TotalSeconds, _options.ExtractionTimeoutSeconds));

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ExtractStatusResponse status;
            try
            {
                status = await _client.Extraction.GetExtractStatus(jobId, ct: cancellationToken);
            }
            catch (SdkException<RawError> ex)
            {
                throw new InvalidOperationException(
                    $"Firecrawl extract-status check failed (HTTP {(int)ex.Error.StatusCode}): {ex.Error.ReadAsString()}", ex);
            }

            if (status.Status == Status4.Completed)
            {
                return new SupplierProductReadResult(ParseProducts(status.Data), ListingFullyCaptured: true);
            }

            if (status.Status == Status4.Failed || status.Status == Status4.Cancelled)
            {
                // Terminal but not fully captured — surface whatever partial data exists (usually none).
                return new SupplierProductReadResult(ParseProducts(status.Data), ListingFullyCaptured: false);
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                // Timed out while still processing — treat as a partial read.
                return new SupplierProductReadResult(ParseProducts(status.Data), ListingFullyCaptured: false);
            }

            await Task.Delay(pollDelay, cancellationToken);
        }
    }

    /// <summary>
    /// JSON Schema describing the structured data we want back: a <c>products</c> array where each
    /// item carries name, description, price, brand, url and imageUrl.
    /// </summary>
    private static object BuildProductSchema() => new
    {
        type = "object",
        properties = new Dictionary<string, object>
        {
            ["products"] = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["name"] = new { type = "string", description = "The product's name/title." },
                        ["description"] = new { type = "string", description = "The product's description." },
                        ["price"] = new { type = "number", description = "The product's price as a number, without currency symbols." },
                        ["brand"] = new { type = "string", description = "The product's brand or manufacturer." },
                        ["url"] = new { type = "string", description = "The URL of the product's own page." },
                        ["imageUrl"] = new { type = "string", description = "The URL of the product's image." }
                    },
                    required = new[] { "name" }
                }
            }
        },
        required = new[] { "products" }
    };

    private static IReadOnlyList<SupplierProduct> ParseProducts(object? data)
    {
        var products = new List<SupplierProduct>();
        if (data is not JsonElement root)
        {
            return products;
        }

        if (!TryGetProductArray(root, out var array))
        {
            return products;
        }

        foreach (var element in array.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var fields = IndexProperties(element);
            var name = GetString(fields, "name", "title", "productName", "product_name");
            var description = GetString(fields, "description", "desc", "summary", "details");
            var brand = GetString(fields, "brand", "manufacturer", "vendor", "make");
            var url = GetString(fields, "url", "productUrl", "product_url", "link", "href");
            var imageUrl = GetString(fields, "imageUrl", "image_url", "image", "img", "thumbnail");
            var price = GetDecimal(fields, "price", "amount", "cost", "value");

            var externalId = FirstNonEmpty(url, GetString(fields, "id", "sku", "productId", "product_id"), name);
            if (string.IsNullOrWhiteSpace(externalId))
            {
                continue;
            }

            products.Add(new SupplierProduct(externalId!, name, description, price, brand, imageUrl));
        }

        return products;
    }

    private static bool TryGetProductArray(JsonElement root, out JsonElement array)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            array = root;
            return true;
        }

        if (root.ValueKind == JsonValueKind.Object)
        {
            if (root.TryGetProperty("products", out var products) && products.ValueKind == JsonValueKind.Array)
            {
                array = products;
                return true;
            }

            // Fall back to the first array-valued property (schema drift tolerance).
            foreach (var property in root.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Array)
                {
                    array = property.Value;
                    return true;
                }
            }
        }

        array = default;
        return false;
    }

    private static Dictionary<string, JsonElement> IndexProperties(JsonElement element)
    {
        var map = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
        {
            map[property.Name] = property.Value;
        }

        return map;
    }

    private static string? GetString(Dictionary<string, JsonElement> fields, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!fields.TryGetValue(key, out var value))
            {
                continue;
            }

            switch (value.ValueKind)
            {
                case JsonValueKind.String:
                    var s = value.GetString();
                    if (!string.IsNullOrWhiteSpace(s))
                    {
                        return s!.Trim();
                    }
                    break;
                case JsonValueKind.Number:
                    return value.GetRawText();
            }
        }

        return null;
    }

    private static decimal? GetDecimal(Dictionary<string, JsonElement> fields, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!fields.TryGetValue(key, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
            {
                return number;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                var parsed = ParsePrice(value.GetString());
                if (parsed.HasValue)
                {
                    return parsed;
                }
            }
        }

        return null;
    }

    /// <summary>Parses a price that may arrive as a formatted string such as "$1,299.00".</summary>
    private static decimal? ParsePrice(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var cleaned = new StringBuilder(raw!.Length);
        foreach (var c in raw)
        {
            if (char.IsDigit(c) || c == '.' || c == '-')
            {
                cleaned.Append(c);
            }
        }

        var text = cleaned.ToString();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static string Describe(ExtractDataError error)
    {
        if (error.TryGetExtract400Error1(out var badRequest))
        {
            return "HTTP 400: " + Serialize(badRequest);
        }

        if (error.TryGetExtract500Error1(out var serverError))
        {
            return "HTTP 500: " + Serialize(serverError);
        }

        if (error.TryGetRawError(out var raw))
        {
            return $"HTTP {(int)raw.StatusCode}: {raw.ReadAsString()}";
        }

        return "unknown error";
    }

    private static string Serialize(object? value)
    {
        try
        {
            return JsonSerializer.Serialize(value);
        }
        catch
        {
            return value?.ToString() ?? "null";
        }
    }
}
