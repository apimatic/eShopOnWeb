using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Typed HTTP client for the Maxio Advanced Billing API.
/// Endpoint shapes per the Billing API docs:
///   GET  /product_families/{id-or-handle:handle}/products.json
///   GET  /customers/lookup.json?reference=...
///   POST /customers.json
///   GET  /subscriptions/lookup.json?reference=...
///   POST /subscriptions.json
///   GET  /customers/{customer_id}/subscriptions.json
/// Authentication is HTTP Basic with the API key as username and "X" as password
/// (configured on the HttpClient in Program.cs).
/// </summary>
public class MaxioClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;

    public MaxioClient(HttpClient httpClient, IOptions<MaxioSettings> settings)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
    }

    public bool IsConfigured => _settings.IsConfigured && _httpClient.BaseAddress is not null;

    /// <summary>Lists the non-archived products (plans) in the configured product family.</summary>
    public async Task<IReadOnlyList<MaxioProduct>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var family = Uri.EscapeDataString($"handle:{_settings.ProductFamilyHandle}");
        var wrappers = await GetAsync<List<MaxioProductWrapper>>(
            $"product_families/{family}/products.json", cancellationToken) ?? new List<MaxioProductWrapper>();
        return wrappers.Select(w => w.Product).Where(p => p.ArchivedAt is null).ToList();
    }

    /// <summary>Returns the customer with the given reference, or null when none exists (404).</summary>
    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var wrapper = await GetAsync<MaxioCustomerWrapper>(
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}",
            cancellationToken, allowNotFound: true);
        return wrapper?.Customer;
    }

    /// <summary>
    /// Creates a customer. If a concurrent request already created a customer with the same
    /// reference (Maxio enforces reference uniqueness with a 422), the existing customer is
    /// looked up and returned instead, keeping the operation idempotent.
    /// </summary>
    public async Task<MaxioCustomer> CreateCustomerAsync(
        string firstName, string lastName, string email, string reference, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var request = new CreateMaxioCustomerRequest
        {
            Customer = new CreateMaxioCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference
            }
        };

        try
        {
            var wrapper = await PostAsync<MaxioCustomerWrapper>("customers.json", request, cancellationToken);
            return wrapper.Customer;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var existing = await FindCustomerByReferenceAsync(reference, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }
            throw;
        }
    }

    /// <summary>Returns the subscription with the given reference, or null when none exists (404).</summary>
    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var wrapper = await GetAsync<MaxioSubscriptionWrapper>(
            $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}",
            cancellationToken, allowNotFound: true);
        return wrapper?.Subscription;
    }

    /// <summary>
    /// Creates a subscription for an existing customer (by reference) to a product (by handle).
    /// The caller-supplied subscription reference makes the operation idempotent: if Maxio
    /// rejects the create because the reference is already taken (e.g. a retried double-click
    /// raced past the pre-check), the existing subscription is returned instead.
    /// </summary>
    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        string productHandle, string customerReference, string subscriptionReference, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var request = new CreateMaxioSubscriptionRequest
        {
            Subscription = new CreateMaxioSubscription
            {
                ProductHandle = productHandle,
                CustomerReference = customerReference,
                Reference = subscriptionReference
            }
        };

        try
        {
            var wrapper = await PostAsync<MaxioSubscriptionWrapper>("subscriptions.json", request, cancellationToken);
            return wrapper.Subscription;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var existing = await FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }
            throw;
        }
    }

    /// <summary>Lists all subscriptions belonging to a Maxio customer.</summary>
    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var wrappers = await GetAsync<List<MaxioSubscriptionWrapper>>(
            $"customers/{customerId}/subscriptions.json", cancellationToken) ?? new List<MaxioSubscriptionWrapper>();
        return wrappers.Select(w => w.Subscription).ToList();
    }

    private void EnsureConfigured()
    {
        if (!IsConfigured)
        {
            throw new MaxioConfigurationException(
                "Maxio integration is not configured. Provide Maxio:ApiKey and either Maxio:BaseUrl or " +
                "Maxio:Subdomain (e.g. via user-secrets or environment variables).");
        }
    }

    private async Task<T?> GetAsync<T>(string relativeUri, CancellationToken cancellationToken, bool allowNotFound = false)
    {
        using var response = await _httpClient.GetAsync(relativeUri, cancellationToken);
        if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
    }

    private async Task<T> PostAsync<T>(string relativeUri, object body, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(relativeUri, body, JsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        return result ?? throw new MaxioApiException(response.StatusCode, "Empty response body");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new MaxioApiException(response.StatusCode, body);
        }
    }
}
