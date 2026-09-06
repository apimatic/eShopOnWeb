using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Typed HTTP client for the Maxio Advanced Billing API, built directly against the operations,
/// parameters, schemas and error models declared in the Maxio OpenAPI specification.
/// </summary>
public class MaxioApiClient : IMaxioApiClient
{
    /// <summary>Maxio caps per_page at 200 (specification parameter "per-page").</summary>
    private const int PageSize = 200;

    /// <summary>Bounds pagination so a misbehaving upstream cannot spin this client forever.</summary>
    private const int MaxPages = 25;

    /// <summary>
    /// Maxio speaks snake_case JSON, and its bodies carry many more fields than this integration
    /// consumes, so unknown members are ignored rather than treated as a contract break.
    /// </summary>
    internal static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<MaxioSite> ReadSiteAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<MaxioSiteResponse>(HttpMethod.Get, "site.json", null, cancellationToken);
        return response?.Site ?? throw new BillingProviderException("Maxio returned an empty site payload.");
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(productFamilyHandle))
        {
            throw new ArgumentException("A product family handle is required.", nameof(productFamilyHandle));
        }

        // The specification documents product_family_id as either the family's numeric id or its
        // handle with a "handle:" prefix. The handle form keeps this integration free of numeric ids,
        // which Maxio reassigns whenever a catalog is re-seeded.
        var familySegment = Uri.EscapeDataString("handle:" + productFamilyHandle.Trim());
        var products = new List<MaxioProduct>();

        for (var page = 1; page <= MaxPages; page++)
        {
            var path = $"product_families/{familySegment}/products.json" +
                       $"?page={page.ToString(CultureInfo.InvariantCulture)}" +
                       $"&per_page={PageSize.ToString(CultureInfo.InvariantCulture)}";

            var pageResults = await SendAsync<List<MaxioProductResponse>>(HttpMethod.Get, path, null, cancellationToken)
                              ?? new List<MaxioProductResponse>();

            products.AddRange(pageResults.Select(p => p.Product).OfType<MaxioProduct>());

            if (pageResults.Count < PageSize)
            {
                return products;
            }
        }

        _logger.LogWarning(
            "Stopped paging Maxio products for family {ProductFamilyHandle} after {MaxPages} pages.",
            productFamilyHandle, MaxPages);

        return products;
    }

    public async Task<MaxioCustomer?> ReadCustomerByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var response = await SendAsync<MaxioCustomerResponse>(
            HttpMethod.Get, path, null, cancellationToken, treatNotFoundAsNull: true);

        return response?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(
        MaxioCreateCustomer customer,
        CancellationToken cancellationToken = default)
    {
        var body = new MaxioCreateCustomerRequest { Customer = customer };
        var response = await SendAsync<MaxioCustomerResponse>(HttpMethod.Post, "customers.json", body, cancellationToken);

        return response?.Customer ?? throw new BillingProviderException("Maxio returned an empty customer payload.");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken = default)
    {
        var path = $"customers/{customerId.ToString(CultureInfo.InvariantCulture)}/subscriptions.json";
        var response = await SendAsync<List<MaxioSubscriptionResponse>>(HttpMethod.Get, path, null, cancellationToken)
                       ?? new List<MaxioSubscriptionResponse>();

        return response.Select(s => s.Subscription).OfType<MaxioSubscription>().ToList();
    }

    public async Task<MaxioSubscription?> FindSubscriptionAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        var path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var response = await SendAsync<MaxioSubscriptionResponse>(
            HttpMethod.Get, path, null, cancellationToken, treatNotFoundAsNull: true);

        return response?.Subscription;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        MaxioCreateSubscription subscription,
        CancellationToken cancellationToken = default)
    {
        var body = new MaxioCreateSubscriptionRequest { Subscription = subscription };
        var response = await SendAsync<MaxioSubscriptionResponse>(
            HttpMethod.Post, "subscriptions.json", body, cancellationToken);

        return response?.Subscription
               ?? throw new BillingProviderException("Maxio returned an empty subscription payload.");
    }

    private async Task<TResponse?> SendAsync<TResponse>(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken,
        bool treatNotFoundAsNull = false)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: SerializerOptions);
        }

        var stopwatch = Stopwatch.StartNew();
        HttpResponseMessage response;

        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException &&
                                   !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Maxio request {Method} {Path} could not be completed.", method, path);
            throw new BillingProviderException(
                "The billing provider could not be reached. Please try again.", innerException: ex);
        }

        using (response)
        {
            stopwatch.Stop();
            _logger.LogInformation(
                "Maxio {Method} {Path} responded {StatusCode} in {ElapsedMilliseconds}ms.",
                method, path, (int)response.StatusCode, stopwatch.ElapsedMilliseconds);

            if (treatNotFoundAsNull && response.StatusCode == HttpStatusCode.NotFound)
            {
                return default;
            }

            if (!response.IsSuccessStatusCode)
            {
                var payload = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new MaxioApiException(method, path, response.StatusCode, ParseErrors(payload));
            }

            if (response.StatusCode == HttpStatusCode.NoContent || response.Content.Headers.ContentLength == 0)
            {
                return default;
            }

            try
            {
                return await response.Content.ReadFromJsonAsync<TResponse>(SerializerOptions, cancellationToken);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Maxio response for {Method} {Path} could not be deserialized.", method, path);
                throw new BillingProviderException(
                    "The billing provider returned a response that could not be understood.", innerException: ex);
            }
        }
    }

    /// <summary>
    /// Extracts the messages from a Maxio error body. The specification models errors either as a
    /// string array (Error Array Response) or as a field-keyed object (Customer Error), so both shapes
    /// are read, and an unrecognised body degrades to the raw text.
    /// </summary>
    internal static IReadOnlyList<string> ParseErrors(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("errors", out var errors))
            {
                return new[] { Truncate(payload) };
            }

            switch (errors.ValueKind)
            {
                case JsonValueKind.Array:
                    return errors.EnumerateArray()
                        .Select(e => Stringify(e).Trim())
                        .Where(e => e.Length > 0)
                        .ToArray();

                case JsonValueKind.Object:
                    return errors.EnumerateObject()
                        .Select(p => $"{p.Name}: {Stringify(p.Value).Trim()}")
                        .ToArray();

                case JsonValueKind.String:
                    var message = errors.GetString()?.Trim();
                    return string.IsNullOrEmpty(message) ? Array.Empty<string>() : new[] { message };

                default:
                    return new[] { Truncate(payload) };
            }
        }
        catch (JsonException)
        {
            return new[] { Truncate(payload) };
        }
    }

    private static string Stringify(JsonElement element) =>
        element.ValueKind == JsonValueKind.String ? element.GetString() ?? string.Empty : element.ToString();

    private static string Truncate(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= 500 ? trimmed : trimmed[..500] + "...";
    }
}
