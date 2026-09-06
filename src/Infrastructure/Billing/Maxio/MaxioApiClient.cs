using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Talks to the Maxio Advanced Billing REST API over HTTP.
/// </summary>
/// <remarks>
/// Every path here is taken from the API surface Maxio publishes (each method names the endpoint it
/// calls) and was exercised against a live sandbox site before being written. The client is a thin
/// transport: it composes URLs, serialises bodies, and converts failures into
/// <see cref="MaxioApiException"/>. All business rules live in <see cref="MaxioSubscriptionService"/>.
/// Authentication -- HTTP Basic with the API key as user name and a literal "x" as password -- is
/// configured once on the injected <see cref="HttpClient"/>.
/// </remarks>
public class MaxioApiClient : IMaxioApiClient
{
    /// <summary>Maxio uses snake_case names, mapped explicitly on the contract types.</summary>
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<MaxioSite> ReadSiteAsync(CancellationToken cancellationToken = default)
    {
        var envelope = await GetAsync<MaxioSiteEnvelope>("site.json", cancellationToken);
        return envelope?.Site ?? throw new BillingProviderException("Maxio returned an empty body for GET site.json.");
    }

    /// <inheritdoc />
    public async Task<MaxioProductFamily?> FindProductFamilyByHandleAsync(string handle, CancellationToken cancellationToken = default)
    {
        // Maxio exposes no lookup-by-handle endpoint for product families, so the (small) list is filtered here.
        var families = await GetAsync<List<MaxioProductFamilyEnvelope>>("product_families.json", cancellationToken);

        return families?
            .Select(f => f.ProductFamily)
            .FirstOrDefault(f => f is not null && string.Equals(f.Handle, handle, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(long productFamilyId, CancellationToken cancellationToken = default)
    {
        var products = await GetAsync<List<MaxioProductEnvelope>>(
            $"product_families/{productFamilyId}/products.json?include_archived=false",
            cancellationToken);

        return products?
            .Select(p => p.Product)
            .Where(p => p is not null)
            .Select(p => p!)
            .ToList() ?? new List<MaxioProduct>();
    }

    /// <inheritdoc />
    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var envelope = await GetAsync<MaxioCustomerEnvelope>(
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}",
            cancellationToken);

        return envelope?.Customer;
    }

    /// <inheritdoc />
    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken = default)
    {
        var envelope = await PostAsync<MaxioCreateCustomerRequest, MaxioCustomerEnvelope>(
            "customers.json",
            new MaxioCreateCustomerRequest { Customer = customer },
            cancellationToken);

        return envelope?.Customer ?? throw new BillingProviderException("Maxio returned an empty body for POST customers.json.");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        var subscriptions = await GetAsync<List<MaxioSubscriptionEnvelope>>(
            $"customers/{customerId}/subscriptions.json",
            cancellationToken);

        return subscriptions?
            .Select(s => s.Subscription)
            .Where(s => s is not null)
            .Select(s => s!)
            .ToList() ?? new List<MaxioSubscription>();
    }

    /// <inheritdoc />
    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken cancellationToken = default)
    {
        var envelope = await PostAsync<MaxioCreateSubscriptionRequest, MaxioSubscriptionEnvelope>(
            "subscriptions.json",
            new MaxioCreateSubscriptionRequest { Subscription = subscription },
            cancellationToken);

        return envelope?.Subscription ?? throw new BillingProviderException("Maxio returned an empty body for POST subscriptions.json.");
    }

    /// <summary>Issues a GET, mapping HTTP 404 to <c>null</c> so lookups read naturally at the call site.</summary>
    private async Task<TResponse?> GetAsync<TResponse>(string path, CancellationToken cancellationToken)
        where TResponse : class
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        using var response = await SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, HttpMethod.Get.Method, path, cancellationToken);
        return await ReadAsync<TResponse>(response, path, cancellationToken);
    }

    private async Task<TResponse?> PostAsync<TRequest, TResponse>(string path, TRequest body, CancellationToken cancellationToken)
        where TResponse : class
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body, options: SerializerOptions)
        };

        using var response = await SendAsync(request, cancellationToken);

        await EnsureSuccessAsync(response, HttpMethod.Post.Method, path, cancellationToken);
        return await ReadAsync<TResponse>(response, path, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException ||
                                   (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw new BillingProviderException(
                $"Could not reach Maxio for {request.Method} {request.RequestUri}. See the inner exception for details.",
                ex);
        }
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, string method, string path, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var errors = await ReadErrorsAsync(response, cancellationToken);
        _logger.LogError(
            "Maxio call {Method} {Path} returned {StatusCode}: {Errors}",
            method,
            path,
            (int)response.StatusCode,
            errors.Count > 0 ? string.Join("; ", errors) : "(no detail)");

        throw new MaxioApiException(response.StatusCode, method, path, errors);
    }

    private static async Task<IReadOnlyList<string>> ReadErrorsAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(body))
            {
                return Array.Empty<string>();
            }

            // Validation failures come back as {"errors": [...]}. Anything else -- an HTML error page from
            // a proxy, say -- is surfaced verbatim but truncated so it stays loggable.
            var parsed = JsonSerializer.Deserialize<MaxioErrorResponse>(body, SerializerOptions);
            if (parsed?.Errors is { Count: > 0 })
            {
                return parsed.Errors;
            }

            return new[] { body.Length > 500 ? body[..500] : body };
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static async Task<TResponse?> ReadAsync<TResponse>(HttpResponseMessage response, string path, CancellationToken cancellationToken)
        where TResponse : class
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<TResponse>(SerializerOptions, cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new BillingProviderException($"Maxio returned a body for '{path}' that could not be parsed.", ex);
        }
    }
}
