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
/// Reads a supplier's product listing with Firecrawl's structured Extract operation.
/// Everything about how we talk to Firecrawl is confined to this adapter; the rest of the
/// app depends only on <see cref="ISupplierProductScraper"/>.
/// </summary>
public class FirecrawlSupplierProductScraper : ISupplierProductScraper
{
    private const string ExtractionPrompt =
        "This is a supplier's product listing page. Extract every product shown on the page. " +
        "For each product capture its name, description, price (as a plain number without any " +
        "currency symbol) and brand, plus the canonical product URL (or a stable unique " +
        "identifier / SKU) that uniquely identifies that product. Return all products found.";

    private readonly FirecrawlApiClient _client;
    private readonly FirecrawlOptions _options;
    private readonly IAppLogger<FirecrawlSupplierProductScraper> _logger;

    public FirecrawlSupplierProductScraper(
        FirecrawlApiClient client,
        IOptions<FirecrawlOptions> options,
        IAppLogger<FirecrawlSupplierProductScraper> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<SupplierScrapeResult> ScrapeListingAsync(string listingUrl, CancellationToken cancellationToken)
    {
        var request = new ExtractRequest
        {
            Urls = new[] { listingUrl },
            Prompt = ExtractionPrompt,
            Schema = BuildProductsSchema(),
        };

        ExtractResponse start;
        try
        {
            start = await _client.Extraction.ExtractData(request, ct: cancellationToken);
        }
        catch (SdkException<ExtractDataError> ex)
        {
            throw new InvalidOperationException(
                $"Firecrawl rejected the extract request for '{listingUrl}': {DescribeExtractDataError(ex.Error)}", ex);
        }

        if (string.IsNullOrWhiteSpace(start.Id) || !Guid.TryParse(start.Id, out var jobId))
            throw new InvalidOperationException(
                $"Firecrawl accepted the extract request for '{listingUrl}' but returned no usable job id.");

        _logger.LogInformation("Firecrawl extract job {0} started for '{1}'.", jobId, listingUrl);

        var data = await PollForExtractDataAsync(jobId, cancellationToken);
        var products = ParseProducts(data);

        _logger.LogInformation("Firecrawl extract job {0} returned {1} product(s).", jobId, products.Count);
        return new SupplierScrapeResult(products);
    }

    private async Task<object?> PollForExtractDataAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, _options.PollIntervalSeconds));
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(Math.Max(1, _options.PollTimeoutSeconds));

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
                    $"Firecrawl extract status check for job {jobId} failed " +
                    $"({(int)ex.Error.StatusCode}): {SafeReadError(ex.Error)}", ex);
            }

            var current = status.Status;
            if (current == Status4.Completed)
                return status.Data;

            if (current == Status4.Failed || current == Status4.Cancelled)
                throw new InvalidOperationException($"Firecrawl extract job {jobId} ended with status '{current}'.");

            if (DateTimeOffset.UtcNow >= deadline)
                throw new TimeoutException(
                    $"Firecrawl extract job {jobId} did not complete within {_options.PollTimeoutSeconds}s.");

            await Task.Delay(interval, cancellationToken);
        }
    }

    /// <summary>
    /// A JSON Schema (per json-schema.org) asking Firecrawl for an array of products.
    /// Built as nested dictionaries so it serializes to exactly the wire shape we want.
    /// </summary>
    private static object BuildProductsSchema() => new Dictionary<string, object?>
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object?>
        {
            ["products"] = new Dictionary<string, object?>
            {
                ["type"] = "array",
                ["items"] = new Dictionary<string, object?>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object?>
                    {
                        ["url"] = PropertySchema("string", "Canonical product URL, or a stable unique identifier / SKU"),
                        ["name"] = PropertySchema("string", "Product name or title"),
                        ["description"] = PropertySchema("string", "Product description"),
                        ["price"] = PropertySchema("number", "Product price as a plain number, no currency symbol"),
                        ["brand"] = PropertySchema("string", "Product brand or manufacturer"),
                    },
                    ["required"] = new[] { "name" },
                },
            },
        },
        ["required"] = new[] { "products" },
    };

    private static Dictionary<string, object?> PropertySchema(string type, string description) =>
        new() { ["type"] = type, ["description"] = description };

    /// <summary>
    /// Reads the untyped <c>data</c> payload (the SDK models it as <c>object?</c>) into product
    /// records, tolerating shape drift: the products may sit under a <c>products</c> key, be the
    /// top-level array itself, or appear under another array-valued property.
    /// </summary>
    private List<ScrapedProduct> ParseProducts(object? data)
    {
        if (data is null)
            return new List<ScrapedProduct>();

        JsonElement root;
        try
        {
            root = data is JsonElement je ? je : JsonSerializer.SerializeToElement(data);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Firecrawl returned data that could not be read as JSON.", ex);
        }

        if (!TryFindProductsArray(root, out var productsArray))
            return new List<ScrapedProduct>();

        var products = new List<ScrapedProduct>();
        foreach (var element in productsArray.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
                continue;

            products.Add(new ScrapedProduct(
                ExternalId: GetString(element, "url") ?? GetString(element, "sku") ?? GetString(element, "id") ?? string.Empty,
                Name: GetString(element, "name") ?? GetString(element, "title"),
                Description: GetString(element, "description"),
                Price: GetDecimal(element, "price"),
                Brand: GetString(element, "brand")));
        }

        return products;
    }

    private static bool TryFindProductsArray(JsonElement root, out JsonElement productsArray)
    {
        productsArray = default;

        if (root.ValueKind == JsonValueKind.Array)
        {
            productsArray = root;
            return true;
        }

        if (root.ValueKind != JsonValueKind.Object)
            return false;

        if (TryGetProperty(root, "products", out var named) && named.ValueKind == JsonValueKind.Array)
        {
            productsArray = named;
            return true;
        }

        // Fall back to the first array-valued property (handles e.g. a differently-named list).
        foreach (var property in root.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Array)
            {
                productsArray = property.Value;
                return true;
            }
        }

        return false;
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
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

    private static string? GetString(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null,
        };
    }

    private static decimal? GetDecimal(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var value))
            return null;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
            return number;

        if (value.ValueKind == JsonValueKind.String)
            return ParsePrice(value.GetString());

        return null;
    }

    /// <summary>Best-effort parse of a price the LLM may have returned as a formatted string.</summary>
    private static decimal? ParsePrice(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var builder = new StringBuilder(raw!.Length);
        foreach (var c in raw)
        {
            if (char.IsDigit(c) || c == '.' || c == '-')
                builder.Append(c);
            // treat a comma as a thousands separator: drop it
        }

        var cleaned = builder.ToString();
        if (cleaned.Length == 0)
            return null;

        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var price)
            ? price
            : (decimal?)null;
    }

    private static string DescribeExtractDataError(ExtractDataError error)
    {
        if (error.TryGetExtract400Error1(out var badRequest))
            return $"400 Bad Request: {badRequest}";
        if (error.TryGetExtract500Error1(out var serverError))
            return $"500 Server Error: {serverError}";
        if (error.TryGetRawError(out var raw))
            return $"{(int)raw.StatusCode}: {SafeReadError(raw)}";
        return "unknown error";
    }

    private static string SafeReadError(RawError raw)
    {
        try
        {
            return raw.ReadAsString();
        }
        catch
        {
            return "<unreadable error body>";
        }
    }
}
