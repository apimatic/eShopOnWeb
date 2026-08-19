using System;
using System.Collections.Generic;
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
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Firecrawl;

/// <summary>
/// Reads a supplier's product listing page via Firecrawl's LLM-driven Extract job flow: start an
/// extract with a products schema, poll until the job completes, then map the extracted JSON into
/// <see cref="SupplierProduct"/> records. The Extract flow is the only typed SDK path that carries a
/// JSON schema, so it is used here deliberately.
/// </summary>
public class FirecrawlSupplierCatalogReader : ISupplierCatalogReader
{
    private const string ExtractionPrompt =
        "This is a supplier's product listing page, which may span multiple pages linked by " +
        "'Next'/pagination links. Extract every product across the whole listing. For each product " +
        "capture its name, description, price as a number, brand, the product's SKU or product code, " +
        "and the product's URL (the link to its own detail page) when present. Return them under a " +
        "top-level 'products' array.";

    private readonly FirecrawlApiClient _client;
    private readonly FirecrawlOptions _options;
    private readonly IAppLogger<FirecrawlSupplierCatalogReader> _logger;

    public FirecrawlSupplierCatalogReader(
        FirecrawlApiClient client,
        IOptions<FirecrawlOptions> options,
        IAppLogger<FirecrawlSupplierCatalogReader> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SupplierProduct>> ReadProductListingAsync(string listingUrl, CancellationToken cancellationToken = default)
    {
        var jobId = await StartExtractAsync(listingUrl, cancellationToken);
        var data = await PollForDataAsync(jobId, listingUrl, cancellationToken);
        var products = ProductJsonMapper.Map(data);
        _logger.LogInformation($"Firecrawl extract for {listingUrl} returned {products.Count} product(s).");
        return products;
    }

    private async Task<Guid> StartExtractAsync(string listingUrl, CancellationToken cancellationToken)
    {
        // A bare URL only extracts that one page; a path-scoped wildcard makes Firecrawl follow the
        // listing's pagination so we capture the whole listing, not just the first page.
        var request = new ExtractRequest
        {
            Urls = new[] { BuildCrawlScope(listingUrl) },
            Prompt = ExtractionPrompt,
            Schema = BuildProductsSchema(),
            ShowSources = false,
        };

        ExtractResponse response;
        try
        {
            response = await _client.Extraction.ExtractData(request, ct: cancellationToken);
        }
        catch (SdkException<ExtractDataError> ex)
        {
            throw new SupplierCatalogReadException($"Firecrawl rejected the extract request: {DescribeExtractError(ex.Error)}", ex);
        }
        catch (Exception ex) when (ex is not SupplierCatalogReadException and not OperationCanceledException)
        {
            throw new SupplierCatalogReadException($"Firecrawl extract request failed: {ex.Message}", ex);
        }

        if (string.IsNullOrWhiteSpace(response.Id) || !Guid.TryParse(response.Id, out var jobId))
        {
            throw new SupplierCatalogReadException("Firecrawl did not return a usable extract job id.");
        }

        return jobId;
    }

    private async Task<object?> PollForDataAsync(Guid jobId, string listingUrl, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + _options.ExtractTimeout;

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
                throw new SupplierCatalogReadException(
                    $"Firecrawl extract-status check failed ({(int)ex.Error.StatusCode}): {Truncate(ex.Error.ReadAsString(), 500)}", ex);
            }
            catch (Exception ex) when (ex is not SupplierCatalogReadException and not OperationCanceledException)
            {
                throw new SupplierCatalogReadException($"Firecrawl extract-status check failed: {ex.Message}", ex);
            }

            if (status.Status == Status4.Completed)
            {
                return status.Data;
            }

            if (status.Status == Status4.Failed || status.Status == Status4.Cancelled)
            {
                throw new SupplierCatalogReadException($"Firecrawl extract job for {listingUrl} ended with status '{status.Status}'.");
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new SupplierCatalogReadException(
                    $"Firecrawl extract job for {listingUrl} did not complete within {_options.ExtractTimeout.TotalSeconds:N0}s (last status '{status.Status}').");
            }

            await Task.Delay(_options.PollInterval, cancellationToken);
        }
    }

    // Scopes the extract to the listing and its subpages (e.g. pagination) by turning the listing
    // URL into a path-scoped wildcard, which Firecrawl crawls. A URL already ending in '*' is left
    // as-is; otherwise the URL's directory is wildcarded.
    private static string BuildCrawlScope(string listingUrl)
    {
        if (listingUrl.EndsWith("*", StringComparison.Ordinal))
        {
            return listingUrl;
        }

        if (!Uri.TryCreate(listingUrl, UriKind.Absolute, out var uri))
        {
            return listingUrl;
        }

        var path = uri.AbsolutePath;
        var lastSlash = path.LastIndexOf('/');
        var directory = lastSlash >= 0 ? path.Substring(0, lastSlash + 1) : "/";
        return $"{uri.Scheme}://{uri.Authority}{directory}*";
    }

    // A JSON-Schema object describing an object with a 'products' array. Lower-cased identifiers keep
    // the serialized keys as JSON Schema expects them.
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
                        name = new { type = "string", description = "The product name" },
                        description = new { type = "string", description = "The product description" },
                        price = new { type = "number", description = "The product price as a number" },
                        brand = new { type = "string", description = "The product brand" },
                        sku = new { type = "string", description = "The product SKU or supplier product code" },
                        url = new { type = "string", description = "The URL of the product's own detail page" }
                    },
                    required = new[] { "name" }
                }
            }
        },
        required = new[] { "products" }
    };

    private static string DescribeExtractError(ExtractDataError error)
    {
        if (error.TryGetExtract400Error1(out var badRequest) && badRequest is not null)
        {
            return $"400 Bad Request: {badRequest.Error ?? "invalid request"}";
        }
        if (error.TryGetExtract500Error1(out var serverError) && serverError is not null)
        {
            return $"500 Server Error: {serverError.Error ?? "server error"}";
        }
        if (error.TryGetRawError(out var raw) && raw is not null)
        {
            return $"{(int)raw.StatusCode}: {Truncate(raw.ReadAsString(), 500)}";
        }
        return "unknown error";
    }

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }
        return value!.Length <= maxLength ? value : value.Substring(0, maxLength) + "…";
    }
}
