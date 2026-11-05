using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Requests.Customers;
using MaxioAdvancedBilling.Requests.ProductFamilies;
using MaxioAdvancedBilling.Requests.Subscriptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Subscription capability backed by Maxio Advanced Billing (system of record).
/// Maxio holds all state: the eShopOnWeb user id is stored as the Maxio customer <c>reference</c>,
/// so the user to customer to subscription mapping survives restarts without local persistence.
/// </summary>
public class MaxioSubscriptionService : ISubscriptionService
{
    private const int PAGE_SIZE = 200;
    private const int LOCK_STRIPES = 64;

    // The SDK stamps a fresh Idempotency-Key on every call, so a double-click is made idempotent here:
    // calls for the same user are serialized and the existing subscription is re-checked inside the lock.
    private static readonly SemaphoreSlim[] s_userLocks =
        Enumerable.Range(0, LOCK_STRIPES).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    private static readonly HashSet<string> s_deadStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create"
    };

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(MaxioAdvancedBillingClient client, IOptions<MaxioSettings> settings,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default) =>
        Guard<IReadOnlyList<SubscriptionPlan>>(async () =>
        {
            var products = await ListProductsAsync(cancellationToken);
            return products.Select(ToPlan).ToList();
        });

    public Task<SubscribeResult> SubscribeAsync(SubscriberIdentity subscriber, string planHandle,
        CancellationToken cancellationToken = default) =>
        Guard(async () =>
        {
            var plan = (await ListProductsAsync(cancellationToken))
                .FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase))
                ?? throw new PlanNotFoundException(planHandle);

            var gate = s_userLocks[(uint)StringComparer.Ordinal.GetHashCode(subscriber.UserId) % LOCK_STRIPES];
            await gate.WaitAsync(cancellationToken);
            try
            {
                var customer = await EnsureCustomerAsync(subscriber, cancellationToken);

                var existing = await ListLiveSubscriptionsAsync(customer.Id!.Value, cancellationToken);
                var current = existing.FirstOrDefault(s =>
                    string.Equals(s.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase));
                if (current != null)
                {
                    _logger.LogInformation("User {UserId} already subscribed to {Plan}; returning subscription {SubscriptionId}",
                        subscriber.UserId, plan.Handle, current.Id);
                    return new SubscribeResult(ToUserSubscription(current), true);
                }

                var created = await _client.Subscriptions.CreateSubscription(new CreateSubscriptionOperationRequest
                {
                    Body = new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            ProductHandle = plan.Handle,
                            CustomerId = customer.Id,
                            // Plans do not require a payment method, so no card is captured: bill by invoice instead of auto-charging.
                            PaymentCollectionMethod = CollectionMethod.Remittance,
                        }
                    }
                }, cancellationToken: cancellationToken);

                var subscription = created.Subscription
                    ?? throw new BillingRequestException("Billing system returned no subscription.", false);
                _logger.LogInformation("Created Maxio subscription {SubscriptionId} for user {UserId} on {Plan}",
                    subscription.Id, subscriber.UserId, plan.Handle);
                return new SubscribeResult(ToUserSubscription(subscription), false);
            }
            finally
            {
                gate.Release();
            }
        });

    public Task<IReadOnlyList<UserSubscription>> ListSubscriptionsAsync(SubscriberIdentity subscriber,
        CancellationToken cancellationToken = default) =>
        Guard<IReadOnlyList<UserSubscription>>(async () =>
        {
            var customer = await FindCustomerAsync(subscriber.UserId, cancellationToken);
            if (customer?.Id == null)
            {
                return Array.Empty<UserSubscription>();
            }

            var subscriptions = await ListCustomerSubscriptionsAsync(customer.Id.Value, cancellationToken);
            return subscriptions
                .Where(s => FamilyMatches(s.Product))
                .OrderByDescending(s => s.CreatedAt)
                .Select(ToUserSubscription)
                .ToList();
        });

    private async Task<Customer?> FindCustomerAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(
                new ReadCustomerByReferenceRequest { Reference = reference }, cancellationToken: ct);
            return response.Customer;
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<Customer> EnsureCustomerAsync(SubscriberIdentity subscriber, CancellationToken ct)
    {
        var customer = await FindCustomerAsync(subscriber.UserId, ct);
        if (customer?.Id != null)
        {
            return customer;
        }

        var localPart = subscriber.Email.Split('@')[0];
        try
        {
            var created = await _client.Customers.CreateCustomer(new CreateCustomerOperationRequest
            {
                Body = new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = string.IsNullOrWhiteSpace(subscriber.FirstName) ? localPart : subscriber.FirstName,
                        LastName = string.IsNullOrWhiteSpace(subscriber.LastName) ? "(eShopOnWeb)" : subscriber.LastName,
                        Email = subscriber.Email,
                        Reference = subscriber.UserId,
                    }
                }
            }, cancellationToken: ct);
            _logger.LogInformation("Created Maxio customer {CustomerId} for user {UserId}", created.Customer.Id, subscriber.UserId);
            return created.Customer;
        }
        catch (ApiException ex) when ((int)ex.StatusCode == 422)
        {
            // Lost a race against another instance creating the same customer: re-read before giving up.
            var raced = await FindCustomerAsync(subscriber.UserId, ct);
            if (raced?.Id != null)
            {
                return raced;
            }
            throw;
        }
    }

    private async Task<List<Product>> ListProductsAsync(CancellationToken ct)
    {
        var result = new List<Product>();
        for (var page = 1; ; page++)
        {
            var batch = await _client.ProductFamilies.ListProductsForProductFamily(new ListProductsForProductFamilyRequest
            {
                ProductFamilyId = "handle:" + _settings.ProductFamilyHandle,
                Page = page,
                PerPage = PAGE_SIZE,
            }, cancellationToken: ct);
            result.AddRange(batch.Select(b => b.Product).Where(p => p.Handle != null && p.ArchivedAt == null));
            if (batch.Count < PAGE_SIZE)
            {
                return result;
            }
        }
    }

    private async Task<List<Subscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct)
    {
        var all = await _client.Customers.ListCustomerSubscriptions(
            new ListCustomerSubscriptionsRequest { CustomerId = customerId }, cancellationToken: ct);
        return all.Select(r => r.Subscription).Where(s => s != null).Select(s => s!).ToList();
    }

    private async Task<List<Subscription>> ListLiveSubscriptionsAsync(int customerId, CancellationToken ct) =>
        (await ListCustomerSubscriptionsAsync(customerId, ct))
            .Where(s => s.State == null || !s_deadStates.Contains(s.State.Value))
            .ToList();

    private bool FamilyMatches(Product? product) =>
        product?.ProductFamily?.Handle == null
        || string.Equals(product.ProductFamily.Handle, _settings.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase);

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey)
            || string.IsNullOrWhiteSpace(_settings.ProductFamilyHandle)
            || (string.IsNullOrWhiteSpace(_settings.Subdomain) && string.IsNullOrWhiteSpace(_settings.BaseUrl)))
        {
            throw new BillingNotConfiguredException(
                "Subscription billing is not configured (Maxio:ApiKey, Maxio:Subdomain and Maxio:ProductFamilyHandle are required).");
        }
    }

    /// <summary>Translates SDK failures into application exceptions; never leaks Maxio payloads to callers.</summary>
    private async Task<T> Guard<T>(Func<Task<T>> action)
    {
        EnsureConfigured();
        try
        {
            return await action();
        }
        catch (SubscriptionException)
        {
            throw;
        }
        catch (ApiException<CreateSubscriptionError> ex) when (ex.Error.TryGetErrorListResponse1(out var list))
        {
            var detail = string.Join("; ", list.Errors);
            _logger.LogWarning(ex, "Maxio rejected subscription creation: {Detail}", detail);
            throw new BillingRequestException($"Billing system rejected the subscription: {detail}", true, ex);
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "Maxio API call failed with HTTP {StatusCode}", (int)ex.StatusCode);
            var status = (int)ex.StatusCode;
            if (status is 401 or 403)
            {
                throw new BillingNotConfiguredException("Billing system rejected the configured credentials.", ex);
            }
            throw new BillingRequestException(
                status == 422 ? "Billing system rejected the request." : "Billing system request failed.",
                status == 422, ex);
        }
        catch (SdkException ex)
        {
            _logger.LogError(ex, "Maxio SDK call failed");
            throw new BillingRequestException("Billing system is unavailable.", false, ex);
        }
    }

    private static SubscriptionPlan ToPlan(Product p) => new(
        p.Handle!, p.Name ?? p.Handle!, p.Description, (p.PriceInCents ?? 0) / 100m, null,
        p.Interval, p.IntervalUnit?.Value);

    private static UserSubscription ToUserSubscription(Subscription s) => new(
        s.Id?.ToString() ?? string.Empty,
        s.Product?.Handle ?? string.Empty,
        s.Product?.Name ?? s.Product?.Handle ?? string.Empty,
        (s.ProductPriceInCents ?? s.Product?.PriceInCents ?? 0) / 100m,
        s.Currency,
        s.Product?.Interval,
        s.Product?.IntervalUnit?.Value,
        s.State?.Value ?? "unknown",
        s.NextAssessmentAt,
        s.CreatedAt);
}
