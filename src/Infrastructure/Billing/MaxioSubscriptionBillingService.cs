using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// <see cref="ISubscriptionBillingService"/> backed by the Maxio Advanced Billing API.
/// Customers are correlated to eShopOnWeb users through the Maxio customer "reference"
/// field, which is unique per site; subscriptions are created with remittance (invoice)
/// collection so signup works without capturing a payment method.
/// </summary>
public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    // Serializes subscribe calls per subscriber within this process so a double-click
    // (or concurrent retry) cannot race past the existing-subscription check.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _subscribeGates = new();

    private static readonly HashSet<string> _activeStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "trialing", "past_due"
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        HttpClient httpClient,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _settings.Validate();
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        // The product family can be addressed by handle using the "handle:" prefix,
        // so no numeric-id lookup is required (ids change when a site is re-seeded).
        using var response = await _httpClient.GetAsync(
            $"product_families/handle:{_settings.ProductFamilyHandle}/products.json", cancellationToken);
        var items = await ReadAsync<List<MaxioProductListItem>>(response, cancellationToken);

        return (items ?? new List<MaxioProductListItem>())
            .Select(i => i.Product)
            .Where(p => p is not null && p.ArchivedAt is null && !string.IsNullOrEmpty(p.Handle))
            .Select(p => new SubscriptionPlan
            {
                Handle = p!.Handle!,
                Name = p.Name ?? string.Empty,
                Description = p.Description,
                PriceInCents = p.PriceInCents,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit ?? string.Empty
            })
            .ToList();
    }

    public async Task<BillingSubscription> SubscribeAsync(
        SubscriberInfo subscriber, string planHandle, CancellationToken cancellationToken = default)
    {
        var gate = _subscribeGates.GetOrAdd(subscriber.Reference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(subscriber, cancellationToken);
            var existing = await ListSubscriptionsAsync(customer.Id, cancellationToken);

            var match = existing.FirstOrDefault(s =>
                s.State is not null && _activeStates.Contains(s.State) &&
                string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase));

            if (match is not null)
            {
                _logger.LogInformation(
                    "Subscriber {Reference} already has an active subscription {SubscriptionId} for plan {PlanHandle}; returning it.",
                    subscriber.Reference, match.Id, planHandle);
                return Map(match);
            }

            var request = new
            {
                subscription = new
                {
                    product_handle = planHandle,
                    customer_reference = subscriber.Reference,
                    payment_collection_method = "remittance"
                }
            };

            using var response = await _httpClient.PostAsJsonAsync("subscriptions.json", request, cancellationToken);
            var created = await ReadAsync<MaxioSubscriptionResponse>(response, cancellationToken);

            _logger.LogInformation(
                "Created subscription {SubscriptionId} for subscriber {Reference} on plan {PlanHandle}.",
                created?.Subscription?.Id, subscriber.Reference, planHandle);

            return Map(created?.Subscription ?? throw new MaxioApiException(
                HttpStatusCode.BadGateway, new[] { "Maxio returned an empty subscription response." }));
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<BillingSubscription>> GetSubscriptionsAsync(
        string customerReference, CancellationToken cancellationToken = default)
    {
        var customer = await FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<BillingSubscription>();
        }

        var subscriptions = await ListSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(Map).ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(SubscriberInfo subscriber, CancellationToken cancellationToken)
    {
        var existing = await FindCustomerByReferenceAsync(subscriber.Reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var request = new
        {
            customer = new
            {
                first_name = subscriber.FirstName,
                last_name = subscriber.LastName,
                email = subscriber.Email,
                reference = subscriber.Reference
            }
        };

        try
        {
            using var response = await _httpClient.PostAsJsonAsync("customers.json", request, cancellationToken);
            var created = await ReadAsync<MaxioCustomerResponse>(response, cancellationToken);
            if (created?.Customer is not null)
            {
                _logger.LogInformation(
                    "Created Maxio customer {CustomerId} for subscriber {Reference}.",
                    created.Customer.Id, subscriber.Reference);
                return created.Customer;
            }

            throw new MaxioApiException(HttpStatusCode.BadGateway, new[] { "Maxio returned an empty customer response." });
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // The reference is unique per site: a concurrent request created the customer first.
            var winner = await FindCustomerByReferenceAsync(subscriber.Reference, cancellationToken);
            if (winner is not null)
            {
                return winner;
            }

            throw;
        }
    }

    private async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var result = await ReadAsync<MaxioCustomerResponse>(response, cancellationToken);
        return result?.Customer;
    }

    private async Task<List<MaxioSubscription>> ListSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"customers/{customerId}/subscriptions.json", cancellationToken);
        var items = await ReadAsync<List<MaxioSubscriptionListItem>>(response, cancellationToken);
        return (items ?? new List<MaxioSubscriptionListItem>())
            .Select(i => i.Subscription)
            .Where(s => s is not null)
            .Select(s => s!)
            .ToList();
    }

    private static BillingSubscription Map(MaxioSubscription subscription)
    {
        return new BillingSubscription
        {
            SubscriptionId = subscription.Id,
            PlanHandle = subscription.Product?.Handle ?? string.Empty,
            PlanName = subscription.Product?.Name ?? string.Empty,
            State = subscription.State ?? string.Empty,
            PriceInCents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? 0,
            Interval = subscription.Product?.Interval ?? 0,
            IntervalUnit = subscription.Product?.IntervalUnit ?? string.Empty,
            Currency = subscription.Currency,
            ActivatedAt = subscription.ActivatedAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            NextBillingAt = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt
        };
    }

    private async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw await ToExceptionAsync(response, cancellationToken);
        }

        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken);
    }

    private static async Task<MaxioApiException> ToExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        List<string> errors = new();
        try
        {
            var errorResponse = await response.Content.ReadFromJsonAsync<MaxioErrorResponse>(cancellationToken: cancellationToken);
            if (errorResponse?.Errors is { Count: > 0 })
            {
                errors = errorResponse.Errors;
            }
        }
        catch (Exception)
        {
            // Non-JSON error body; fall through to the generic message below.
        }

        if (errors.Count == 0)
        {
            errors.Add($"Maxio API returned {(int)response.StatusCode} ({response.ReasonPhrase}).");
        }

        return new MaxioApiException(response.StatusCode, errors);
    }
}
