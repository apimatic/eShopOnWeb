using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using FirecrawlApi;
using FirecrawlApi.Core.ErrorResponse;
using FirecrawlApi.Core.Exceptions;
using FirecrawlApi.Errors;
using FirecrawlApi.Models;
using FirecrawlApi.Models.Enums;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Firecrawl;

/// <summary>
/// Reads a supplier's product listing page with Firecrawl's LLM-driven Extract endpoint. Because the
/// SDK's single-page scrape path cannot express schema-driven JSON extraction, this uses the dedicated
/// asynchronous Extract job (submit + poll) which models a JSON schema and prompt as first-class inputs.
/// </summary>
public class FirecrawlSupplierCatalogReader : ISupplierCatalogReader
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan JobTimeout = TimeSpan.FromMinutes(3);

    private readonly FirecrawlApiClient _client;
    private readonly IAppLogger<FirecrawlSupplierCatalogReader> _logger;

    public FirecrawlSupplierCatalogReader(FirecrawlApiClient client, IAppLogger<FirecrawlSupplierCatalogReader> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<SupplierListingReadResult> ReadListingAsync(string listingUrl, CancellationToken cancellationToken = default)
    {
        var jobId = await SubmitExtractJobAsync(listingUrl, cancellationToken);
        var data = await PollUntilCompleteAsync(jobId, cancellationToken);
        var products = ParseProducts(data);

        _logger.LogInformation($"Firecrawl extract job {jobId} yielded {products.Count} product(s) from {listingUrl}.");

        // The Extract job either completes (whole page read) or fails; there is no truncation signal.
        // A completed job means the listing was fully captured. Partial outcomes then arise downstream
        // when individual products cannot be imported.
        return new SupplierListingReadResult(products, listingFullyCaptured: true);
    }

    private async Task<Guid> SubmitExtractJobAsync(string listingUrl, CancellationToken cancellationToken)
    {
        var request = new ExtractRequest
        {
            Urls = new[] { listingUrl },
            Prompt =
                "Extract every product shown on this supplier's product listing page. For each product, " +
                "capture its name, a description, its price as a numeric value, its brand, and the product's " +
                "own detail-page URL. Return them all in the 'products' array; do not omit any product on the page.",
            Schema = BuildProductListSchema()
        };

        ExtractResponse response;
        try
        {
            response = await _client.Extraction.ExtractData(request, ct: cancellationToken);
        }
        catch (SdkException<ExtractDataError> ex)
        {
            throw new SupplierCatalogReadException($"Firecrawl rejected the extract request: {DescribeError(ex.Error)}", ex);
        }
        catch (SdkException<RawError> ex)
        {
            throw new SupplierCatalogReadException($"Firecrawl rejected the extract request: {DescribeRawError(ex.Error)}", ex);
        }
        catch (Exception ex) when (ex is not SupplierCatalogReadException and not OperationCanceledException)
        {
            throw new SupplierCatalogReadException($"Firecrawl extract request failed: {ex.Message}", ex);
        }

        if (response.Success == false || string.IsNullOrWhiteSpace(response.Id))
            throw new SupplierCatalogReadException("Firecrawl accepted the request but returned no extract job id.");

        if (response.InvalidUrLs is { Count: > 0 } invalid && invalid.Contains(listingUrl))
            throw new SupplierCatalogReadException($"Firecrawl considered the listing URL invalid: {listingUrl}");

        if (!Guid.TryParse(response.Id, out var jobId))
            throw new SupplierCatalogReadException($"Firecrawl returned an unrecognized extract job id: '{response.Id}'.");

        return jobId;
    }

    private async Task<object?> PollUntilCompleteAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + JobTimeout;

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
                throw new SupplierCatalogReadException($"Firecrawl extract status check failed: {DescribeRawError(ex.Error)}", ex);
            }
            catch (Exception ex) when (ex is not SupplierCatalogReadException and not OperationCanceledException)
            {
                throw new SupplierCatalogReadException($"Firecrawl extract status check failed: {ex.Message}", ex);
            }

            if (status.Status == Status4.Completed)
                return status.Data;

            if (status.Status == Status4.Failed || status.Status == Status4.Cancelled)
                throw new SupplierCatalogReadException($"Firecrawl extract job {jobId} ended with status '{status.Status?.Value}'.");

            if (DateTimeOffset.UtcNow >= deadline)
                throw new SupplierCatalogReadException($"Firecrawl extract job {jobId} did not complete within {JobTimeout.TotalSeconds:N0}s.");

            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    /// <summary>
    /// JSON Schema requesting a top-level object with a <c>products</c> array. Built with explicit lower-case
    /// keys so it serializes correctly regardless of the SDK's property-naming policy.
    /// </summary>
    private static JsonObject BuildProductListSchema()
    {
        JsonObject StringProp() => new() { ["type"] = "string" };

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["products"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["name"] = StringProp(),
                            ["description"] = StringProp(),
                            ["price"] = new JsonObject { ["type"] = "number" },
                            ["brand"] = StringProp(),
                            ["url"] = StringProp()
                        },
                        ["required"] = new JsonArray { "name" }
                    }
                }
            },
            ["required"] = new JsonArray { "products" }
        };
    }

    private IReadOnlyList<ScrapedProduct> ParseProducts(object? data)
    {
        if (data is not JsonElement root)
        {
            // System.Text.Json boxes an `object?` payload as JsonElement; anything else means no data.
            _logger.LogWarning("Firecrawl extract job completed but returned no structured data.");
            return Array.Empty<ScrapedProduct>();
        }

        var array = LocateProductArray(root);
        if (array is null)
        {
            _logger.LogWarning("Firecrawl extract data did not contain a recognizable products array.");
            return Array.Empty<ScrapedProduct>();
        }

        var products = new List<ScrapedProduct>();
        foreach (var element in array.Value.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
                continue;

            products.Add(new ScrapedProduct
            {
                Name = GetString(element, "name", "title", "productName"),
                Description = GetString(element, "description", "summary", "desc"),
                Price = GetPrice(element, "price", "amount", "cost"),
                Brand = GetString(element, "brand", "manufacturer", "vendor"),
                ExternalId = GetString(element, "url", "link", "productUrl", "id", "sku")
            });
        }

        return products;
    }

    /// <summary>Finds the products array whether the payload is the array itself, an object with a
    /// <c>products</c> key, or an object whose only array-valued property holds the products.</summary>
    private static JsonElement? LocateProductArray(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
            return root;

        if (root.ValueKind != JsonValueKind.Object)
            return null;

        if (root.TryGetProperty("products", out var products) && products.ValueKind == JsonValueKind.Array)
            return products;

        foreach (var candidate in new[] { "items", "data", "results" })
        {
            if (root.TryGetProperty(candidate, out var value) && value.ValueKind == JsonValueKind.Array)
                return value;
        }

        // Fall back to the first array-valued property on the object.
        foreach (var property in root.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Array)
                return property.Value;
        }

        return null;
    }

    private static string? GetString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (TryGetPropertyIgnoreCase(element, name, out var value))
            {
                if (value.ValueKind == JsonValueKind.String)
                {
                    var s = value.GetString();
                    if (!string.IsNullOrWhiteSpace(s))
                        return s;
                }
                else if (value.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
                {
                    return value.ToString();
                }
            }
        }

        return null;
    }

    private static decimal? GetPrice(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGetPropertyIgnoreCase(element, name, out var value))
                continue;

            if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
                return number;

            if (value.ValueKind == JsonValueKind.String && TryParsePrice(value.GetString(), out var parsed))
                return parsed;
        }

        return null;
    }

    private static bool TryParsePrice(string? raw, out decimal price)
    {
        price = 0m;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        // Keep digits, decimal separator and sign; drop currency symbols, thousands separators and text.
        var cleaned = new string(raw.Where(c => char.IsDigit(c) || c == '.' || c == '-').ToArray());
        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out price) && price > 0;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
    {
        if (element.TryGetProperty(name, out value))
            return true;

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string DescribeError(ExtractDataError error)
    {
        if (error.TryGetRawError(out var raw))
            return DescribeRawError(raw);

        return "the request was rejected.";
    }

    private static string DescribeRawError(RawError error)
    {
        var body = error.ReadAsString();
        var trimmed = string.IsNullOrWhiteSpace(body) ? "(no body)" : body.Trim();
        if (trimmed.Length > 500)
            trimmed = trimmed.Substring(0, 500);
        return $"HTTP {(int)error.StatusCode} — {trimmed}";
    }
}
