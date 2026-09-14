using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public sealed class MaxioBillingService : IMaxioBillingService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscriptionLocks = new();
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;

    public MaxioBillingService(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken)
    {
        var family = Uri.EscapeDataString($"handle:{_options.ProductFamilyHandle}");
        var products = await GetAsync<List<ProductEnvelope>>(
            $"product_families/{family}/products.json?per_page=200&include_archived=false",
            "list subscription plans",
            cancellationToken);

        return products
            .Select(item => item.Product)
            .Where(product => product.ArchivedAt is null && !string.IsNullOrWhiteSpace(product.Handle))
            .Select(ToPlan)
            .OrderBy(product => product.PriceInCents)
            .ToList();
    }

    public async Task<UserSubscription?> SubscribeAsync(
        MaxioUser user,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var normalizedHandle = productHandle.Trim();
        var subscriptionReference = BuildSubscriptionReference(user.Id, normalizedHandle);
        var gate = SubscriptionLocks.GetOrAdd(subscriptionReference, _ => new SemaphoreSlim(1, 1));

        await gate.WaitAsync(cancellationToken);
        try
        {
            var plans = await GetPlansAsync(cancellationToken);
            if (!plans.Any(plan => string.Equals(plan.Handle, normalizedHandle, StringComparison.Ordinal)))
            {
                return null;
            }

            var existing = await FindSubscriptionAsync(subscriptionReference, cancellationToken);
            if (existing is not null)
            {
                return ToSubscription(existing);
            }

            var customer = await EnsureCustomerAsync(user, cancellationToken);
            var request = new CreateSubscriptionEnvelope
            {
                Subscription = new CreateSubscription
                {
                    CustomerId = customer.Id,
                    ProductHandle = normalizedHandle,
                    Reference = subscriptionReference
                }
            };

            using var response = await _httpClient.PostAsJsonAsync(
                _options.BuildUri("subscriptions.json"),
                request,
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var created = await ReadRequiredAsync<SubscriptionEnvelope>(response, "create subscription", cancellationToken);
                return ToSubscription(created.Subscription);
            }

            // A response may be lost after Maxio commits the signup. Re-read by the
            // deterministic reference before treating a retryable/validation response as failure.
            existing = await FindSubscriptionAsync(subscriptionReference, cancellationToken);
            if (existing is not null)
            {
                return ToSubscription(existing);
            }

            throw new MaxioApiException("create subscription", (int)response.StatusCode);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<UserSubscription>> GetSubscriptionsAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        var customer = await FindCustomerAsync(userId, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<UserSubscription>();
        }

        var subscriptions = await GetAsync<List<SubscriptionEnvelope>>(
            $"customers/{customer.Id}/subscriptions.json",
            "list customer subscriptions",
            cancellationToken);

        return subscriptions
            .Select(item => ToSubscription(item.Subscription))
            .OrderByDescending(item => item.Id)
            .ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(MaxioUser user, CancellationToken cancellationToken)
    {
        var existing = await FindCustomerAsync(user.Id, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var request = new CreateCustomerEnvelope
        {
            Customer = new CreateCustomer
            {
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,
                Reference = user.Id
            }
        };

        using var response = await _httpClient.PostAsJsonAsync(
            _options.BuildUri("customers.json"),
            request,
            cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            var created = await ReadRequiredAsync<CustomerEnvelope>(response, "create customer", cancellationToken);
            return created.Customer;
        }

        // Customer references are unique in Maxio. If another request created the
        // customer concurrently, the lookup turns that race into a successful read.
        existing = await FindCustomerAsync(user.Id, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        throw new MaxioApiException("create customer", (int)response.StatusCode);
    }

    private async Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            _options.BuildUri($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}"),
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var envelope = await ReadRequiredAsync<CustomerEnvelope>(response, "find customer", cancellationToken);
        return envelope.Customer;
    }

    private async Task<MaxioSubscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            _options.BuildUri($"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}"),
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var envelope = await ReadRequiredAsync<SubscriptionEnvelope>(response, "find subscription", cancellationToken);
        return envelope.Subscription;
    }

    private async Task<T> GetAsync<T>(string path, string operation, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(_options.BuildUri(path), cancellationToken);
        return await ReadRequiredAsync<T>(response, operation, cancellationToken);
    }

    private static async Task<T> ReadRequiredAsync<T>(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new MaxioApiException(operation, (int)response.StatusCode);
        }

        var content = await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken);
        return content ?? throw new MaxioApiException(operation, (int)response.StatusCode);
    }

    private static string BuildSubscriptionReference(string userId, string productHandle)
        => $"eshop:{userId}:{productHandle}";

    private static SubscriptionPlan ToPlan(MaxioProduct product)
        => new(
            product.Handle!,
            product.Name,
            product.Description,
            product.PriceInCents,
            product.Interval,
            product.IntervalUnit,
            product.RequireCreditCard);

    private static UserSubscription ToSubscription(MaxioSubscription subscription)
        => new(
            subscription.Id,
            subscription.Product?.Handle ?? string.Empty,
            subscription.Product?.Name ?? string.Empty,
            subscription.ProductPriceInCents,
            subscription.Product?.Interval ?? 0,
            subscription.Product?.IntervalUnit ?? string.Empty,
            subscription.State,
            subscription.CurrentPeriodEndsAt);
}
