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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// <see cref="ISubscriptionService"/> backed by Maxio Advanced Billing (system of record).
/// The eShopOnWeb user id is stored as the Maxio customer <c>reference</c>, so no local mapping table is
/// needed and the relationship survives restarts / in-memory databases.
/// </summary>
public class MaxioSubscriptionService : ISubscriptionService
{
    // Subscription states that no longer represent a live subscription; subscribing again is allowed.
    private static readonly HashSet<string> EndedStates = new(StringComparer.OrdinalIgnoreCase)
    {
        SubscriptionState.Canceled.Value,
        SubscriptionState.Expired.Value,
        SubscriptionState.FailedToCreate.Value,
    };

    // Serializes concurrent subscribe calls for the same user (double-click) within this process.
    private static readonly SemaphoreSlim[] UserLocks =
        Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

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

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var family = RequireFamilyHandle();
        var plans = new List<SubscriptionPlan>();
        const int perPage = 100;

        for (var page = 1; ; page++)
        {
            var products = await CallAsync(() => _client.ProductFamilies.ListProductsForProductFamily(
                new ListProductsForProductFamilyRequest
                {
                    ProductFamilyId = $"handle:{family}",
                    Page = page,
                    PerPage = perPage,
                }, cancellationToken: cancellationToken));

            plans.AddRange(products
                .Select(p => p.Product)
                .Where(p => p.ArchivedAt is null && !string.IsNullOrEmpty(p.Handle))
                .Select(p => new SubscriptionPlan(
                    p.Handle!,
                    p.Name ?? p.Handle!,
                    string.IsNullOrWhiteSpace(p.Description) ? null : p.Description,
                    p.PriceInCents ?? 0,
                    p.Interval ?? 1,
                    p.IntervalUnit?.Value ?? "month")));

            if (products.Count < perPage) break;
        }

        return plans;
    }

    public async Task<SubscribeResult> SubscribeAsync(Subscriber subscriber, string planHandle,
        CancellationToken cancellationToken = default)
    {
        var plan = (await ListPlansAsync(cancellationToken))
            .FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.Ordinal))
            ?? throw new SubscriptionPlanNotFoundException(planHandle);

        var gate = UserLocks[(uint)subscriber.UserId.GetHashCode() % UserLocks.Length];
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customerId = await EnsureCustomerAsync(subscriber, cancellationToken);

            var existing = (await ListCustomerSubscriptionsAsync(customerId, cancellationToken))
                .FirstOrDefault(s => string.Equals(s.PlanHandle, plan.Handle, StringComparison.Ordinal)
                                     && !EndedStates.Contains(s.State));
            if (existing is not null)
            {
                _logger.LogInformation("User {UserId} already subscribed to {Plan}; returning subscription {SubscriptionId}",
                    subscriber.UserId, plan.Handle, existing.SubscriptionId);
                return new SubscribeResult(existing, Created: false);
            }

            var response = await CallAsync(() => _client.Subscriptions.CreateSubscription(
                new CreateSubscriptionOperationRequest
                {
                    Body = new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            ProductHandle = plan.Handle,
                            CustomerId = customerId,
                            PaymentCollectionMethod = ResolveCollectionMethod(),
                        }
                    }
                }, cancellationToken: cancellationToken));

            var created = Map(response.Subscription
                ?? throw new BillingUnavailableException("Billing returned an empty subscription response."));
            _logger.LogInformation("Created Maxio subscription {SubscriptionId} for user {UserId} on plan {Plan}",
                created.SubscriptionId, subscriber.UserId, plan.Handle);
            return new SubscribeResult(created, Created: true);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<UserSubscription>> ListSubscriptionsAsync(Subscriber subscriber,
        CancellationToken cancellationToken = default)
    {
        var customerId = await FindCustomerIdAsync(subscriber.UserId, cancellationToken);
        if (customerId is null) return Array.Empty<UserSubscription>();
        return await ListCustomerSubscriptionsAsync(customerId.Value, cancellationToken);
    }

    private async Task<int?> FindCustomerIdAsync(string userId, CancellationToken cancellationToken)
    {
        var response = await CallAsync(() => _client.Customers.ReadCustomerByReference(
            new ReadCustomerByReferenceRequest { Reference = userId }, cancellationToken: cancellationToken),
            notFoundIsExpected: true);
        return response?.Customer.Id;
    }

    /// <summary>Finds the Maxio customer for the user or creates it. Never creates a second customer for one user.</summary>
    private async Task<int> EnsureCustomerAsync(Subscriber subscriber, CancellationToken cancellationToken)
    {
        var id = await FindCustomerIdAsync(subscriber.UserId, cancellationToken);
        if (id is not null) return id.Value;

        var (first, last) = SplitName(subscriber.Email);
        try
        {
            var created = await CallAsync(() => _client.Customers.CreateCustomer(new CreateCustomerOperationRequest
            {
                Body = new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = first,
                        LastName = last,
                        Email = subscriber.Email,
                        Reference = subscriber.UserId,
                    }
                }
            }, cancellationToken: cancellationToken));
            _logger.LogInformation("Created Maxio customer {CustomerId} for user {UserId}", created.Customer.Id, subscriber.UserId);
            return created.Customer.Id
                ?? throw new BillingUnavailableException("Billing returned a customer without an id.");
        }
        catch (SubscriptionRejectedException)
        {
            // Possibly lost a race with another instance creating the same customer: use theirs.
            id = await FindCustomerIdAsync(subscriber.UserId, cancellationToken);
            if (id is not null) return id.Value;
            throw;
        }
    }

    private async Task<IReadOnlyList<UserSubscription>> ListCustomerSubscriptionsAsync(int customerId,
        CancellationToken cancellationToken)
    {
        var subscriptions = await CallAsync(() => _client.Customers.ListCustomerSubscriptions(
            new ListCustomerSubscriptionsRequest { CustomerId = customerId }, cancellationToken: cancellationToken));
        return subscriptions
            .Where(s => s.Subscription is not null)
            .Select(s => Map(s.Subscription!))
            .ToList();
    }

    private static UserSubscription Map(Subscription s) => new(
        SubscriptionId: s.Id?.ToString() ?? string.Empty,
        PlanHandle: s.Product?.Handle ?? string.Empty,
        PlanName: s.Product?.Name ?? s.Product?.Handle ?? string.Empty,
        PriceInCents: s.ProductPriceInCents ?? s.Product?.PriceInCents ?? 0,
        Interval: s.Product?.Interval ?? 1,
        IntervalUnit: s.Product?.IntervalUnit?.Value ?? "month",
        State: s.State?.Value ?? "unknown",
        NextBillingAt: s.NextAssessmentAt ?? s.CurrentPeriodEndsAt,
        CreatedAt: s.CreatedAt);

    // Maxio requires first and last name; eShopOnWeb users only have an email/username.
    private static (string First, string Last) SplitName(string email)
    {
        var local = email.Split('@')[0].Trim();
        var parts = local.Split(new[] { '.', '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var first = parts.Length > 0 ? Capitalize(parts[0]) : "Shopper";
        var last = parts.Length > 1 ? Capitalize(string.Join(" ", parts.Skip(1))) : "(eShopOnWeb)";
        return (first, last);
    }

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private CollectionMethod ResolveCollectionMethod()
    {
        var configured = string.IsNullOrWhiteSpace(_settings.PaymentCollectionMethod)
            ? "remittance"
            : _settings.PaymentCollectionMethod.Trim().ToLowerInvariant();
        return CollectionMethod.TryGetKnownValue(configured, out var method)
            ? method
            : throw new BillingUnavailableException($"Billing is misconfigured: unknown 'Maxio:PaymentCollectionMethod' '{configured}'.");
    }

    private string RequireFamilyHandle() =>
        string.IsNullOrWhiteSpace(_settings.ProductFamilyHandle)
            ? throw new BillingUnavailableException("Billing is not configured: 'Maxio:ProductFamilyHandle' is missing.")
            : _settings.ProductFamilyHandle!;

    /// <summary>
    /// Runs an SDK call and translates SDK failures into domain exceptions so no Maxio types leak out.
    /// With <paramref name="notFoundIsExpected"/> a 404 yields null instead of an error.
    /// </summary>
    private async Task<T?> CallAsync<T>(Func<Task<T>> call, bool notFoundIsExpected) where T : class
    {
        try
        {
            return await call();
        }
        catch (ApiException ex) when (notFoundIsExpected && ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (ApiException ex)
        {
            throw Translate(ex);
        }
        catch (SdkException ex)
        {
            _logger.LogError(ex, "Maxio call failed");
            throw new BillingUnavailableException("The billing system is currently unreachable.", ex);
        }
    }

    private async Task<T> CallAsync<T>(Func<Task<T>> call) where T : class =>
        (await CallAsync(call, notFoundIsExpected: false))!;

    private Exception Translate(ApiException ex)
    {
        _logger.LogWarning("Maxio returned HTTP {Status}", (int)ex.StatusCode);
        return ex.StatusCode switch
        {
            HttpStatusCode.UnprocessableEntity => new SubscriptionRejectedException(ExtractMessage(ex)),
            _ => new BillingUnavailableException($"The billing system returned an unexpected error (HTTP {(int)ex.StatusCode}).", ex),
        };
    }

    private static string ExtractMessage(ApiException ex)
    {
        IReadOnlyList<string>? errors = null;
        if (ex is ApiException<CreateSubscriptionError> sub && sub.Error.TryGetErrorListResponse1(out var list))
            errors = list.Errors;
        else if (ex is ApiException<CreateCustomerError> cust && cust.Error.TryGetCustomerErrorResponse1(out var c)
                 && c.Errors is { } e && e.TryGetListOfString(out var strings))
            errors = strings;

        return errors is { Count: > 0 }
            ? string.Join("; ", errors)
            : "The billing system rejected the request.";
    }
}
