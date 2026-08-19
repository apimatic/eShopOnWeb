using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using FirecrawlApi;                        // FirecrawlApiClient
using FirecrawlApi.Models;                 // ExtractRequest, ExtractResponse, ExtractStatusResponse
using FirecrawlApi.Models.Enums;           // Status4
using FirecrawlApi.Core.Exceptions;        // SdkException<TError>
using FirecrawlApi.Core.ErrorResponse;     // RawError
using FirecrawlApi.Errors;                 // ExtractDataError

namespace Microsoft.eShopWeb.Infrastructure.Services.Firecrawl;

/// <summary>
/// Reads a supplier product listing via Firecrawl's job-based Extract endpoint:
/// <c>ExtractData</c> starts a job, then <c>GetExtractStatus</c> is polled until the job reaches a
/// terminal state. Structured extraction is driven by a JSON Schema (the scrape/crawl typed models
/// cannot carry an extraction schema in this SDK), and the untyped result payload is parsed
/// defensively into <see cref="ScrapedProduct"/> instances.
/// </summary>
public class FirecrawlSupplierCatalogScraper : ISupplierCatalogScraper
{
    private static readonly TimeSpan PollDelay = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan OverallDeadline = TimeSpan.FromMinutes(5);

    private readonly FirecrawlApiClient _client;
    private readonly IAppLogger<FirecrawlSupplierCatalogScraper> _logger;

    public FirecrawlSupplierCatalogScraper(
        FirecrawlApiClient client,
        IAppLogger<FirecrawlSupplierCatalogScraper> logger)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<SupplierScrapeResult> ScrapeListingAsync(
        string listingUrl,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(listingUrl))
        {
            throw new ArgumentException("A listing URL is required.", nameof(listingUrl));
        }

        _logger.LogInformation("Firecrawl extract starting for listing {ListingUrl}.", listingUrl);

        var request = new ExtractRequest
        {
            Urls = BuildUrls(listingUrl),
            Prompt =
                "Extract EVERY product listed across ALL pages of this listing, following pagination " +
                "(for example 'Next' links) until no further pages remain. For each product return its " +
                "name, description, price (a numeric value, or null when there is no usable numeric price " +
                "such as 'Contact for pricing'), brand, sku, and url.",
            Schema = BuildProductsSchema(),
        };

        // --- Start the extract job (Case A: SdkException<ExtractDataError>). ---
        ExtractResponse startResponse;
        try
        {
            startResponse = await _client.Extraction
                .ExtractData(request, ct: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (SdkException<ExtractDataError> ex)
        {
            throw TranslateExtractDataError(ex);
        }
        catch (JsonException ex)
        {
            // A JsonException here means Firecrawl's non-2xx error body did not match the generated
            // ExtractDataError shape (the error object could not be constructed) OR a 2xx body drifted.
            // Either way the request could not be completed — surface it as a rejection, NOT an outage,
            // so a caller does not treat a deterministic failure as a retryable 5xx.
            throw new FirecrawlScrapeException(
                "Firecrawl returned an extract response that could not be processed.", statusCode: null, inner: ex);
        }
        catch (HttpRequestException ex)
        {
            throw new FirecrawlScrapeException("Firecrawl was unreachable while starting the extract job.", statusCode: null, inner: ex);
        }

        if (startResponse is null || startResponse.Success != true || string.IsNullOrWhiteSpace(startResponse.Id))
        {
            throw new FirecrawlScrapeException("Firecrawl did not start an extract job (no job id returned).");
        }

        if (!Guid.TryParse(startResponse.Id, out var jobId))
        {
            throw new FirecrawlScrapeException($"Firecrawl returned an unparseable extract job id: '{startResponse.Id}'.");
        }

        // --- Poll until terminal, bounded by an overall deadline linked to the caller's token. ---
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCts.CancelAfter(OverallDeadline);
        var pollToken = linkedCts.Token;

        ExtractStatusResponse status;
        try
        {
            while (true)
            {
                try
                {
                    status = await _client.Extraction
                        .GetExtractStatus(jobId, ct: pollToken)
                        .ConfigureAwait(false);
                }
                catch (SdkException<RawError> ex)  // GetExtractStatus is Case B.
                {
                    throw TranslateRawError("polling the extract status", ex);
                }
                catch (JsonException ex)
                {
                    throw new FirecrawlScrapeException(
                        "Firecrawl returned an extract-status response that could not be processed.", statusCode: null, inner: ex);
                }
                catch (HttpRequestException ex)
                {
                    throw new FirecrawlScrapeException("Firecrawl was unreachable while polling the extract job.", statusCode: null, inner: ex);
                }

                var jobStatus = status.Status;

                if (jobStatus == Status4.Completed)
                {
                    break;
                }

                if (jobStatus == Status4.Failed || jobStatus == Status4.Cancelled)
                {
                    throw new FirecrawlScrapeException(
                        $"Firecrawl extract job {jobId} ended with status '{jobStatus?.Value ?? "unknown"}'.");
                }

                // Processing (or an unknown/absent status): wait, then poll again.
                await Task.Delay(PollDelay, pollToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The overall deadline fired (not the caller's token).
            throw new FirecrawlScrapeException(
                $"Firecrawl extract job {jobId} did not complete within {OverallDeadline.TotalMinutes:0} minutes.");
        }

        var products = MapProducts(status.Data);

        _logger.LogInformation(
            "Firecrawl extract {JobId} completed with {ProductCount} product(s) from {ListingUrl}.",
            jobId, products.Count, listingUrl);

        // We only leave the loop on Completed; Failed/Cancelled throw above.
        return new SupplierScrapeResult(products, listingFullyCaptured: true);
    }

    /// <summary>
    /// Submits both the exact listing URL and an origin wildcard (<c>scheme://host/*</c>) so Firecrawl
    /// follows pagination across the whole listing. Falls back to the listing URL alone if it cannot be
    /// parsed as an absolute URI.
    /// </summary>
    private static IReadOnlyList<string> BuildUrls(string listingUrl)
    {
        var urls = new List<string> { listingUrl };

        if (Uri.TryCreate(listingUrl, UriKind.Absolute, out var uri))
        {
            var wildcard = $"{uri.Scheme}://{uri.Authority}/*";
            if (!string.Equals(wildcard, listingUrl, StringComparison.OrdinalIgnoreCase))
            {
                urls.Add(wildcard);
            }
        }

        return urls;
    }

    /// <summary>
    /// A JSON Schema (json-schema.org) describing an object with a <c>products</c> array. Assigned to the
    /// untyped <see cref="ExtractRequest.Schema"/> and serialized by the SDK's System.Text.Json pipeline.
    /// </summary>
    private static object BuildProductsSchema() => new
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
                        price = new { type = new[] { "number", "null" } },
                        brand = new { type = "string" },
                        sku = new { type = "string" },
                        url = new { type = "string" },
                    },
                    required = new[] { "name" },
                },
            },
        },
        required = new[] { "products" },
    };

    /// <summary>
    /// Maps the untyped extract result into products. The payload is round-tripped through JSON and read
    /// defensively — any field may be absent, and <c>price</c> may arrive as a number or a string.
    /// </summary>
    private static List<ScrapedProduct> MapProducts(object? data)
    {
        var products = new List<ScrapedProduct>();
        if (data is null)
        {
            return products;
        }

        JsonDocument doc;
        try
        {
            var json = JsonSerializer.Serialize(data);
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            // The provider marked the job Completed but the payload was not usable JSON: return what we
            // have (nothing) rather than failing the whole sync on an already-successful job.
            return products;
        }

        using (doc)
        {
            var root = doc.RootElement;

            JsonElement items;
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("products", out var productsEl)
                && productsEl.ValueKind == JsonValueKind.Array)
            {
                items = productsEl;
            }
            else if (root.ValueKind == JsonValueKind.Array)
            {
                items = root;
            }
            else
            {
                return products;
            }

            foreach (var element in items.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                products.Add(new ScrapedProduct
                {
                    Name = ReadString(element, "name"),
                    Description = ReadString(element, "description"),
                    Brand = ReadString(element, "brand"),
                    ExternalId = ReadString(element, "sku"),
                    Url = ReadString(element, "url"),
                    Price = ReadPrice(element, "price"),
                });
            }
        }

        return products;
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            _ => null,
        };
    }

    private static decimal? ReadPrice(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        switch (value.ValueKind)
        {
            case JsonValueKind.Number:
                return value.TryGetDecimal(out var number) ? number : (decimal?)null;
            case JsonValueKind.String:
                return ParsePriceString(value.GetString());
            default:
                return null;
        }
    }

    /// <summary>
    /// Parses a display price such as <c>"$189.99"</c> or <c>"1,299.00"</c> to a decimal, returning
    /// <c>null</c> when there is no usable numeric value (e.g. <c>"Contact for pricing"</c>).
    /// </summary>
    private static decimal? ParsePriceString(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var sb = new StringBuilder(raw.Length);
        foreach (var c in raw)
        {
            if (char.IsDigit(c) || c == '.' || c == '-')
            {
                sb.Append(c);
            }
            // ',' is dropped as a thousands separator.
        }

        var cleaned = sb.ToString();
        if (cleaned.Length == 0)
        {
            return null;
        }

        return decimal.TryParse(
                cleaned,
                NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out var result)
            ? result
            : (decimal?)null;
    }

    // --- Error translation: carry the provider's HTTP status; keep 4xx rejections distinct from outages. ---

    private static FirecrawlScrapeException TranslateExtractDataError(SdkException<ExtractDataError> ex)
    {
        if (ex.Error.TryGetExtract400Error1(out var badRequest))
        {
            return new FirecrawlScrapeException(
                $"Firecrawl rejected the extract request (HTTP 400): {Describe(badRequest)}", statusCode: 400, inner: ex);
        }

        if (ex.Error.TryGetExtract500Error1(out var serverError))
        {
            return new FirecrawlScrapeException(
                $"Firecrawl failed to process the extract request (HTTP 500): {Describe(serverError)}", statusCode: 500, inner: ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            var code = (int)raw.StatusCode;
            return new FirecrawlScrapeException(
                $"Firecrawl returned an error (HTTP {code}): {SafeBody(raw)}", statusCode: code, inner: ex);
        }

        return new FirecrawlScrapeException("Firecrawl returned an unrecognized extract error.", statusCode: null, inner: ex);
    }

    private static FirecrawlScrapeException TranslateRawError(string action, SdkException<RawError> ex)
    {
        var raw = ex.Error;
        var code = (int)raw.StatusCode;
        return new FirecrawlScrapeException(
            $"Firecrawl error while {action} (HTTP {code}): {SafeBody(raw)}", statusCode: code, inner: ex);
    }

    private static string Describe(object errorBody)
    {
        try
        {
            return JsonSerializer.Serialize(errorBody);
        }
        catch (JsonException)
        {
            return errorBody.ToString() ?? "(no detail)";
        }
    }

    private static string SafeBody(RawError raw)
    {
        try
        {
            return raw.ReadAsString();
        }
        catch (Exception)
        {
            return "(unreadable body)";
        }
    }
}

/// <summary>
/// Raised when the Firecrawl extract flow cannot produce a result. <see cref="StatusCode"/> carries the
/// provider's HTTP status when one is known (a 4xx is a deterministic rejection the caller can act on; a
/// <c>null</c> status is a transport/unknown failure). The caller marks the sync as Failed.
/// </summary>
public class FirecrawlScrapeException : Exception
{
    public FirecrawlScrapeException(string message, int? statusCode = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }

    public int? StatusCode { get; }
}
