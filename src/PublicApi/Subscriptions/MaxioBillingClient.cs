using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioBillingClient : IMaxioBillingClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;

    public MaxioBillingClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<MaxioPlan>> ListPlansAsync(CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var family = Uri.EscapeDataString(_options.ProductFamilyHandle!);
        var items = await GetAsync<List<MaxioProductListItem>>($"product_families/handle:{family}/products.json?per_page=200", cancellationToken);
        var plans = new List<MaxioPlan>();
        foreach (var item in items ?? [])
        {
            if (item.Product?.Handle is null || item.Product.Name is null || item.Product.IntervalUnit is null)
                continue;

            plans.Add(new MaxioPlan(
                item.Product.Handle,
                item.Product.Name,
                item.Product.PriceInCents,
                item.Product.Interval,
                item.Product.IntervalUnit,
                item.Product.ProductPricePointHandle));
        }

        return plans;
    }

    public async Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        using var response = await _httpClient.GetAsync($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        await EnsureSuccessAsync(response);
        var envelope = await response.Content.ReadFromJsonAsync<MaxioCustomerEnvelope>(JsonOptions, cancellationToken);
        return envelope?.Customer is { Id: > 0 } customer
            ? new MaxioCustomer(customer.Id, customer.Reference ?? reference)
            : null;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(string reference, string firstName, string lastName, string email, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var body = new
        {
            customer = new
            {
                first_name = firstName,
                last_name = lastName,
                email,
                reference
            }
        };
        using var response = await _httpClient.PostAsJsonAsync("customers.json", body, JsonOptions, cancellationToken);
        await EnsureSuccessAsync(response);
        var envelope = await response.Content.ReadFromJsonAsync<MaxioCustomerEnvelope>(JsonOptions, cancellationToken);
        if (envelope?.Customer is not { Id: > 0 } customer)
            throw new MaxioApiException("Maxio returned a customer response without a customer id.", response.StatusCode);
        return new MaxioCustomer(customer.Id, customer.Reference ?? reference);
    }

    public async Task<MaxioSubscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        using var response = await _httpClient.GetAsync($"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        await EnsureSuccessAsync(response);
        var envelope = await response.Content.ReadFromJsonAsync<MaxioSubscriptionEnvelope>(JsonOptions, cancellationToken);
        return envelope?.Subscription is { Id: > 0 } subscription ? Map(subscription) : null;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(string customerReference, string subscriptionReference, string planHandle, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var body = new
        {
            subscription = new
            {
                product_handle = planHandle,
                customer_reference = customerReference,
                // The sample catalog is configured to allow signup without a payment profile.
                // Invoice collection is the documented no-card collection mode.
                payment_collection_method = "invoice",
                reference = subscriptionReference
            }
        };
        using var response = await _httpClient.PostAsJsonAsync("subscriptions.json", body, JsonOptions, cancellationToken);
        await EnsureSuccessAsync(response);
        var envelope = await response.Content.ReadFromJsonAsync<MaxioSubscriptionEnvelope>(JsonOptions, cancellationToken);
        if (envelope?.Subscription is not { Id: > 0 } subscription)
            throw new MaxioApiException("Maxio returned a subscription response without a subscription id.", response.StatusCode);
        return Map(subscription);
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var items = await GetAsync<List<MaxioSubscriptionListItem>>($"customers/{customerId}/subscriptions.json", cancellationToken);
        var subscriptions = new List<MaxioSubscription>();
        foreach (var item in items ?? [])
        {
            if (item.Subscription is not null)
                subscriptions.Add(Map(item.Subscription));
        }
        return subscriptions;
    }

    private async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
    }

    private void EnsureConfigured()
    {
        _options.Validate();
    }

    private static MaxioSubscription Map(MaxioSubscriptionWire source) => new(
        source.Id,
        source.Reference,
        source.State ?? "unknown",
        source.PriceInCents,
        source.CurrentPeriodEndsAt,
        source.NextAssessmentAt,
        source.Product?.Handle,
        source.Product?.Name,
        source.Product?.ProductPricePointHandle);

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;

        var detail = await response.Content.ReadAsStringAsync();
        throw new MaxioApiException($"Maxio Billing API returned {(int)response.StatusCode}: {detail}", response.StatusCode);
    }
}

public sealed class MaxioApiException : Exception
{
    public MaxioApiException(string message, System.Net.HttpStatusCode statusCode) : base(message)
    {
        StatusCode = statusCode;
    }

    public System.Net.HttpStatusCode StatusCode { get; }
}
