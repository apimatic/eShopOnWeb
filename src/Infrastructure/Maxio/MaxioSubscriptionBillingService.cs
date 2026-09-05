using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Maxio Advanced Billing-backed implementation of <see cref="ISubscriptionBillingService"/>.
/// </summary>
public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    // Serializes subscribe attempts for the same customer+plan so a double-click (two near-simultaneous
    // requests hitting this same process) can never race past the "does it already exist?" check and
    // create two Maxio subscriptions. Keyed by subscription reference; entries are cheap and never removed,
    // which is fine for the number of distinct (user, plan) pairs a demo app will ever see.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscribeLocks = new();

    private readonly MaxioApiClient _client;
    private readonly MaxioOptions _options;

    public MaxioSubscriptionBillingService(MaxioApiClient client, IOptions<MaxioOptions> options)
    {
        _client = client;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        _client.EnsureConfigured();

        var family = await _client.GetProductFamilyByHandleAsync(_options.ProductFamilyHandle, cancellationToken);
        var products = await _client.ListProductsAsync(cancellationToken);

        return products
            .Where(p => p.ProductFamily?.Id == family.Id && p.ArchivedAt is null)
            .Select(MapPlan)
            .ToList();
    }

    public async Task<CustomerSubscription> SubscribeAsync(string customerReference, string customerEmail, string planHandle, CancellationToken cancellationToken = default)
    {
        _client.EnsureConfigured();

        var subscriptionReference = BuildSubscriptionReference(customerReference, planHandle);
        var subscribeLock = SubscribeLocks.GetOrAdd(subscriptionReference, _ => new SemaphoreSlim(1, 1));

        await subscribeLock.WaitAsync(cancellationToken);
        try
        {
            var existing = await _client.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
            if (existing is not null)
            {
                return MapSubscription(existing);
            }

            var plans = await ListPlansAsync(cancellationToken);
            if (!plans.Any(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase)))
            {
                throw new SubscriptionPlanNotFoundException(planHandle);
            }

            var customer = await FindOrCreateCustomerAsync(customerReference, customerEmail, cancellationToken);

            var created = await _client.CreateSubscriptionAsync(new CreateSubscriptionAttributes
            {
                ProductHandle = planHandle,
                CustomerId = customer.Id,
                Reference = subscriptionReference
            }, cancellationToken);

            return MapSubscription(created);
        }
        finally
        {
            subscribeLock.Release();
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(string customerReference, CancellationToken cancellationToken = default)
    {
        _client.EnsureConfigured();

        var customer = await _client.FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(MapSubscription).ToList();
    }

    private async Task<MaxioCustomer> FindOrCreateCustomerAsync(string customerReference, string customerEmail, CancellationToken cancellationToken)
    {
        var existing = await _client.FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = SplitNameFromEmail(customerEmail);
        try
        {
            return await _client.CreateCustomerAsync(new CreateCustomerAttributes
            {
                FirstName = firstName,
                LastName = lastName,
                Email = customerEmail,
                Reference = customerReference
            }, cancellationToken);
        }
        catch (BillingProviderException)
        {
            // Maxio enforces a unique reference per customer. If a concurrent request (e.g. from another
            // process) won the race and created the customer first, this create call 422s - fall back to
            // the customer that now exists rather than surfacing a spurious failure.
            var racedCustomer = await _client.FindCustomerByReferenceAsync(customerReference, cancellationToken);
            if (racedCustomer is not null)
            {
                return racedCustomer;
            }

            throw;
        }
    }

    internal static string BuildSubscriptionReference(string customerReference, string planHandle) =>
        $"eshoponweb:{customerReference}:{planHandle}";

    internal static (string FirstName, string LastName) SplitNameFromEmail(string email)
    {
        var localPart = email.Split('@', 2)[0];
        var segments = localPart.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);

        return segments.Length >= 2
            ? (Capitalize(segments[0]), Capitalize(segments[^1]))
            : (Capitalize(localPart), "Customer");
    }

    private static string Capitalize(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value[1..];

    private static SubscriptionPlan MapPlan(MaxioProduct product) => new()
    {
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? string.Empty,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? string.Empty
    };

    private static CustomerSubscription MapSubscription(MaxioSubscription subscription) => new()
    {
        SubscriptionId = subscription.Id,
        State = subscription.State ?? string.Empty,
        PlanHandle = subscription.Product?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? string.Empty,
        PriceInCents = subscription.Product?.PriceInCents ?? 0,
        Interval = subscription.Product?.Interval ?? 0,
        IntervalUnit = subscription.Product?.IntervalUnit ?? string.Empty,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingAt = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
        CreatedAt = subscription.CreatedAt
    };
}
