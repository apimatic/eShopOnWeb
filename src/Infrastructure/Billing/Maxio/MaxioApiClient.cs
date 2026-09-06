using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Typed <see cref="HttpClient"/> over the Maxio Advanced Billing API. Authentication, retries and
/// the base address are supplied by the handler pipeline configured in
/// <see cref="MaxioBillingServiceCollectionExtensions"/>; this class is only responsible for
/// building spec-correct requests and turning responses into contract types or a
/// <see cref="MaxioApiException"/>.
/// </summary>
public sealed class MaxioApiClient : IMaxioApiClient
{
    /// <summary>Maximum <c>per_page</c> the specification accepts for paged list operations.</summary>
    private const int MaxPageSize = 200;

    /// <summary>Safety stop so a misbehaving upstream cannot spin the pager forever.</summary>
    private const int MaxPages = 25;

    internal static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        // Every contract property is pinned with [JsonPropertyName]; the policy is a safety net
        // for any field added later without an explicit attribute.
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
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
        var response = await SendAsync<MaxioSiteResponse>(
            HttpMethod.Get,
            "site.json",
            content: null,
            operation: "readSite",
            allowNotFound: false,
            cancellationToken);

        return response?.Site
               ?? throw new BillingProviderException(
                   "Maxio returned no site in the readSite response body.");
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(
        string productFamilyIdOrHandle,
        bool includeArchived = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productFamilyIdOrHandle);

        var products = new List<MaxioProduct>();

        for (var page = 1; page <= MaxPages; page++)
        {
            var path = string.Create(
                CultureInfo.InvariantCulture,
                $"product_families/{Uri.EscapeDataString(productFamilyIdOrHandle)}/products.json?page={page}&per_page={MaxPageSize}&include_archived={(includeArchived ? "true" : "false")}");

            var pageItems = await SendAsync<List<MaxioProductResponse>>(
                HttpMethod.Get,
                path,
                content: null,
                operation: "listProductsForProductFamily",
                allowNotFound: false,
                cancellationToken) ?? new List<MaxioProductResponse>();

            foreach (var item in pageItems)
            {
                if (item.Product is not null)
                {
                    products.Add(item.Product);
                }
            }

            if (pageItems.Count < MaxPageSize)
            {
                return products;
            }
        }

        _logger.LogWarning(
            "Stopped paging Maxio products for family {ProductFamily} after {MaxPages} pages.",
            productFamilyIdOrHandle,
            MaxPages);

        return products;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);

        var response = await SendAsync<MaxioCustomerResponse>(
            HttpMethod.Get,
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}",
            content: null,
            operation: "readCustomerByReference",
            allowNotFound: true,
            cancellationToken);

        return response?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(
        MaxioCreateCustomer customer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(customer);

        var response = await SendAsync<MaxioCustomerResponse>(
            HttpMethod.Post,
            "customers.json",
            new MaxioCreateCustomerRequest { Customer = customer },
            operation: "createCustomer",
            allowNotFound: false,
            cancellationToken);

        return response?.Customer
               ?? throw new BillingProviderException(
                   "Maxio accepted createCustomer but returned no customer in the response body.");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        long customerId,
        CancellationToken cancellationToken = default)
    {
        var wrappers = await SendAsync<List<MaxioSubscriptionResponse>>(
            HttpMethod.Get,
            string.Create(CultureInfo.InvariantCulture, $"customers/{customerId}/subscriptions.json"),
            content: null,
            operation: "listCustomerSubscriptions",
            allowNotFound: true,
            cancellationToken);

        if (wrappers is null)
        {
            return Array.Empty<MaxioSubscription>();
        }

        var subscriptions = new List<MaxioSubscription>(wrappers.Count);
        foreach (var wrapper in wrappers)
        {
            if (wrapper.Subscription is not null)
            {
                subscriptions.Add(wrapper.Subscription);
            }
        }

        return subscriptions;
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);

        var response = await SendAsync<MaxioSubscriptionResponse>(
            HttpMethod.Get,
            $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}",
            content: null,
            operation: "findSubscription",
            allowNotFound: true,
            cancellationToken);

        return response?.Subscription;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        MaxioCreateSubscription subscription,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        var response = await SendAsync<MaxioSubscriptionResponse>(
            HttpMethod.Post,
            "subscriptions.json",
            new MaxioCreateSubscriptionRequest { Subscription = subscription },
            operation: "createSubscription",
            allowNotFound: false,
            cancellationToken);

        return response?.Subscription
               ?? throw new BillingProviderException(
                   "Maxio accepted createSubscription but returned no subscription in the response body.");
    }

    /// <summary>
    /// Issues one request and deserialises the response, mapping every failure onto a
    /// <see cref="BillingProviderException"/> so no <see cref="HttpRequestException"/> or
    /// <see cref="JsonException"/> escapes the infrastructure layer.
    /// </summary>
    /// <param name="allowNotFound">
    /// When true a 404 is a normal "no such record" answer and yields <c>null</c> rather than an
    /// exception. The lookup operations of the specification document only a 200 body, and the
    /// sandbox answers a miss with an empty 404.
    /// </param>
    private async Task<TResponse?> SendAsync<TResponse>(
        HttpMethod method,
        string relativePath,
        object? content,
        string operation,
        bool allowNotFound,
        CancellationToken cancellationToken)
        where TResponse : class
    {
        using var request = new HttpRequestMessage(method, relativePath);

        if (content is not null)
        {
            // Buffered as a string rather than JsonContent so the request body can be replayed
            // safely if the resilience handler retries.
            var payload = JsonSerializer.Serialize(content, content.GetType(), SerializerOptions);
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new BillingProviderException(
                $"Maxio operation '{operation}' could not reach the billing API: {ex.Message}",
                statusCode: null,
                errors: null,
                innerException: ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new BillingProviderException(
                $"Maxio operation '{operation}' timed out.",
                statusCode: (int)HttpStatusCode.GatewayTimeout,
                errors: null,
                innerException: ex);
        }

        using (response)
        {
            if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogDebug("Maxio operation {Operation} returned 404; treating as no match.", operation);
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await ReadBodyAsync(response, cancellationToken);
                var errors = MaxioErrorParser.Parse(errorBody);

                _logger.LogError(
                    "Maxio operation {Operation} failed: {StatusCode} {Errors}",
                    operation,
                    (int)response.StatusCode,
                    string.Join(" | ", errors));

                throw new MaxioApiException(operation, response.StatusCode, errors);
            }

            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                return null;
            }

            var body = await ReadBodyAsync(response, cancellationToken);
            if (string.IsNullOrWhiteSpace(body))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<TResponse>(body, SerializerOptions);
            }
            catch (JsonException ex)
            {
                throw new BillingProviderException(
                    $"Maxio operation '{operation}' returned a body that does not match the schema in the specification.",
                    statusCode: (int)response.StatusCode,
                    errors: null,
                    innerException: ex);
            }
        }
    }

    private static async Task<string> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        await response.Content.ReadAsStringAsync(cancellationToken);
}
