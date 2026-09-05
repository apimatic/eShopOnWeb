using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public interface IMaxioBillingClient
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken);
    Task<SubscriptionSummary> EnrollAsync(MaxioShopper shopper, string planHandle, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionSummary>> ListSubscriptionsAsync(MaxioShopper shopper, CancellationToken cancellationToken);
}

/// <summary>Serializes repeat enrollments for a shopper and plan in this service process.</summary>
public sealed class SubscriptionEnrollmentCoordinator
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public async Task<T> RunAsync<T>(string key, Func<Task<T>> action)
    {
        var gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            return await action();
        }
        finally
        {
            gate.Release();
        }
    }
}

/// <summary>
/// Minimal typed client for the documented Maxio Billing API endpoints used by subscriptions.
/// No Maxio identifier is hard-coded: product-family and product handles are resolved at runtime.
/// </summary>
public sealed class MaxioBillingClient : IMaxioBillingClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> NonLiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create", "on_hold", "suspended", "trial_ended"
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly SubscriptionEnrollmentCoordinator _enrollmentCoordinator;

    public MaxioBillingClient(
        HttpClient httpClient,
        IOptions<MaxioOptions> options,
        SubscriptionEnrollmentCoordinator enrollmentCoordinator)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _enrollmentCoordinator = enrollmentCoordinator;
        _httpClient.BaseAddress = _options.GetBaseAddress();
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var path = $"product_families/{Uri.EscapeDataString($"handle:{_options.ProductFamilyHandle}")}/products.json?per_page=200";
        var response = await _httpClient.GetAsync(path, cancellationToken);
        await EnsureSuccessAsync(response);
        var products = await response.Content.ReadFromJsonAsync<List<MaxioProductEnvelope>>(JsonOptions, cancellationToken)
            ?? new List<MaxioProductEnvelope>();

        return products
            .Where(item => item.Product.ArchivedAt is null && !string.IsNullOrWhiteSpace(item.Product.Handle))
            .Select(item => new SubscriptionPlan(item.Product.Handle!, item.Product.Name, item.Product.PriceInCents, item.Product.Interval, item.Product.IntervalUnit))
            .OrderBy(plan => plan.PriceInCents)
            .ToList();
    }

    public Task<SubscriptionSummary> EnrollAsync(MaxioShopper shopper, string planHandle, CancellationToken cancellationToken)
    {
        var key = $"{shopper.UserId}:{planHandle}";
        return _enrollmentCoordinator.RunAsync(key, async () =>
        {
            var plans = await ListPlansAsync(cancellationToken);
            if (!plans.Any(plan => string.Equals(plan.Handle, planHandle, StringComparison.Ordinal)))
            {
                throw new UnknownPlanException();
            }

            var customer = await GetOrCreateCustomerAsync(shopper, cancellationToken);
            var existing = await ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var active = existing.FirstOrDefault(subscription =>
                string.Equals(subscription.Product?.Handle, planHandle, StringComparison.Ordinal)
                && !NonLiveStates.Contains(subscription.State));
            if (active is not null)
            {
                return active.ToSummary();
            }

            var request = new CreateMaxioSubscriptionRequest
            {
                Subscription = new CreateMaxioSubscription
                {
                    ProductHandle = planHandle,
                    CustomerId = customer.Id,
                    Reference = $"eshoponweb-subscription:{shopper.UserId}:{planHandle}"
                }
            };
            using var response = await _httpClient.PostAsJsonAsync("subscriptions.json", request, JsonOptions, cancellationToken);
            await EnsureSuccessAsync(response);
            var created = await response.Content.ReadFromJsonAsync<MaxioSubscriptionEnvelope>(JsonOptions, cancellationToken)
                ?? throw new InvalidOperationException("Maxio Advanced Billing returned an empty subscription response.");
            return created.Subscription.ToSummary();
        });
    }

    public async Task<IReadOnlyList<SubscriptionSummary>> ListSubscriptionsAsync(MaxioShopper shopper, CancellationToken cancellationToken)
    {
        var customer = await LookupCustomerAsync(shopper.CustomerReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionSummary>();
        }

        var subscriptions = await ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(subscription => subscription.ToSummary()).ToList();
    }

    private async Task<MaxioCustomer> GetOrCreateCustomerAsync(MaxioShopper shopper, CancellationToken cancellationToken)
    {
        var existing = await LookupCustomerAsync(shopper.CustomerReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var request = new CreateMaxioCustomerRequest
        {
            Customer = new CreateMaxioCustomer
            {
                FirstName = shopper.FirstName,
                LastName = shopper.LastName,
                Email = shopper.Email,
                Reference = shopper.CustomerReference
            }
        };
        using var response = await _httpClient.PostAsJsonAsync("customers.json", request, JsonOptions, cancellationToken);
        if (response.StatusCode == HttpStatusCode.UnprocessableEntity || response.StatusCode == HttpStatusCode.Conflict)
        {
            // Customer reference is unique in Maxio. A concurrent request may have won the create race.
            var concurrentCustomer = await LookupCustomerAsync(shopper.CustomerReference, cancellationToken);
            if (concurrentCustomer is not null)
            {
                return concurrentCustomer;
            }
        }

        await EnsureSuccessAsync(response);
        var created = await response.Content.ReadFromJsonAsync<MaxioCustomerEnvelope>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Maxio Advanced Billing returned an empty customer response.");
        return created.Customer;
    }

    private async Task<MaxioCustomer?> LookupCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response);
        var envelope = await response.Content.ReadFromJsonAsync<MaxioCustomerEnvelope>(JsonOptions, cancellationToken);
        return envelope?.Customer;
    }

    private async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"customers/{customerId}/subscriptions.json", cancellationToken);
        await EnsureSuccessAsync(response);
        var subscriptions = await response.Content.ReadFromJsonAsync<List<MaxioSubscriptionEnvelope>>(JsonOptions, cancellationToken);
        return subscriptions?.Select(item => item.Subscription).ToList() ?? new List<MaxioSubscription>();
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            // Consume the response so its connection can be reused; upstream details
            // may contain customer data and are intentionally neither logged nor returned.
            await response.Content.LoadIntoBufferAsync();
            throw new MaxioApiException(response.StatusCode);
        }
    }
}

public sealed class UnknownPlanException : Exception
{
    public UnknownPlanException() : base("The requested subscription plan is not available.") { }
}
