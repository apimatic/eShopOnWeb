using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Subscription billing backed by Maxio Advanced Billing (https://{subdomain}.chargify.com).
/// Maxio is the system of record for customers and subscriptions. An eShopOnWeb user is correlated
/// to a Maxio customer through the customer <c>reference</c> attribute, which stores the user id.
/// </summary>
public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private const string RemittanceCollectionMethod = "remittance";

    private static readonly HashSet<string> LiveSubscriptionStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "trialing", "assessing", "soft_failure", "past_due",
        "suspended", "on_hold", "unpaid", "trial_ended", "paused"
    };

    private readonly MaxioApiClient _client;
    private readonly MaxioSettings _settings;
    private readonly KeyedAsyncLock _locks = new();

    public MaxioSubscriptionBillingService(MaxioApiClient client, IOptions<MaxioSettings> settings)
    {
        _client = client;
        _settings = settings.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var family = await GetProductFamilyAsync(cancellationToken).ConfigureAwait(false);
        var products = await _client.ListProductsForProductFamilyAsync(family.Id, cancellationToken).ConfigureAwait(false);
        var currency = await GetSiteCurrencyAsync(cancellationToken).ConfigureAwait(false);

        return products
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p => new SubscriptionPlan
            {
                Id = p.Id,
                Handle = p.Handle ?? string.Empty,
                Name = p.Name ?? string.Empty,
                Description = p.Description,
                PriceInCents = p.PriceInCents,
                Currency = currency,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit ?? string.Empty
            })
            .ToList();
    }

    public async Task<SubscriptionPurchase> SubscribeAsync(
        SubscriberProfile customer,
        string planHandle,
        CancellationToken cancellationToken = default)
    {
        if (customer is null)
        {
            throw new ArgumentNullException(nameof(customer));
        }

        if (string.IsNullOrWhiteSpace(customer.Reference))
        {
            throw new ArgumentException("A customer reference is required to subscribe.", nameof(customer));
        }

        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new ArgumentException("A plan handle is required.", nameof(planHandle));
        }

        var family = await GetProductFamilyAsync(cancellationToken).ConfigureAwait(false);
        var products = await _client.ListProductsForProductFamilyAsync(family.Id, cancellationToken).ConfigureAwait(false);
        var product = products.FirstOrDefault(p =>
            string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
        if (product is null)
        {
            throw new SubscriptionPlanNotFoundException(planHandle);
        }

        var maxioCustomer = await EnsureCustomerAsync(customer, cancellationToken).ConfigureAwait(false);

        // Serialize creation per (customer, plan) so two rapid requests (a double-click) can never
        // create two subscriptions. After acquiring the lock we re-check for an existing live
        // subscription before creating a new one.
        using (await _locks.LockAsync($"{maxioCustomer.Id}:{planHandle}", cancellationToken).ConfigureAwait(false))
        {
            var existing = await FindLiveSubscriptionAsync(maxioCustomer.Id, planHandle, cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                return MapSubscription(existing, alreadySubscribed: true);
            }

            var created = await _client.CreateSubscriptionAsync(new MaxioSubscriptionFields
            {
                ProductHandle = product.Handle,
                CustomerReference = customer.Reference,
                PaymentCollectionMethod = RemittanceCollectionMethod
            }, cancellationToken).ConfigureAwait(false);

            return MapSubscription(created, alreadySubscribed: false);
        }
    }

    public async Task<IReadOnlyList<SubscriptionPurchase>> ListSubscriptionsAsync(
        string customerReference,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(customerReference))
        {
            return Array.Empty<SubscriptionPurchase>();
        }

        var maxioCustomer = await _client.FindCustomerByReferenceAsync(customerReference, cancellationToken).ConfigureAwait(false);
        if (maxioCustomer is null)
        {
            return Array.Empty<SubscriptionPurchase>();
        }

        var subscriptions = await _client.ListCustomerSubscriptionsAsync(maxioCustomer.Id, cancellationToken).ConfigureAwait(false);
        return subscriptions.Select(s => MapSubscription(s, alreadySubscribed: false)).ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(SubscriberProfile profile, CancellationToken cancellationToken)
    {
        var existing = await _client.FindCustomerByReferenceAsync(profile.Reference, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        DeriveNames(profile, out var firstName, out var lastName);

        try
        {
            return await _client.CreateCustomerAsync(new MaxioCustomerFields
            {
                FirstName = firstName,
                LastName = lastName,
                Email = profile.Email,
                Reference = profile.Reference
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 422)
        {
            // Another concurrent request created the same customer (reference is unique in Maxio).
            // Fall back to the lookup so idempotency is preserved.
            var createdByPeer = await _client.FindCustomerByReferenceAsync(profile.Reference, cancellationToken).ConfigureAwait(false);
            if (createdByPeer is not null)
            {
                return createdByPeer;
            }

            throw;
        }
    }

    private async Task<MaxioSubscription?> FindLiveSubscriptionAsync(int customerId, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customerId, cancellationToken).ConfigureAwait(false);
        return subscriptions.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase) &&
            s.State is not null &&
            LiveSubscriptionStates.Contains(s.State));
    }

    private async Task<MaxioProductFamily> GetProductFamilyAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.ProductFamilyHandle))
        {
            throw new MaxioConfigurationException(
                "Maxio is not configured. Provide Maxio:ProductFamilyHandle via user-secrets or environment configuration.");
        }

        var families = await _client.ListProductFamiliesAsync(cancellationToken).ConfigureAwait(false);
        var family = families.FirstOrDefault(f =>
            string.Equals(f.Handle, _settings.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase));
        if (family is null)
        {
            throw new MaxioConfigurationException(
                $"The Maxio product family '{_settings.ProductFamilyHandle}' was not found on the configured site.");
        }

        return family;
    }

    private async Task<string> GetSiteCurrencyAsync(CancellationToken cancellationToken)
    {
        var site = await _client.GetSiteAsync(cancellationToken).ConfigureAwait(false);
        return site.Currency ?? "USD";
    }

    private static void DeriveNames(SubscriberProfile profile, out string firstName, out string lastName)
    {
        var givenFirst = string.IsNullOrWhiteSpace(profile.FirstName) ? null : profile.FirstName;
        var givenLast = string.IsNullOrWhiteSpace(profile.LastName) ? null : profile.LastName;

        var localPart = profile.Email?.Split('@')[0].Trim() ?? string.Empty;

        if (givenFirst is not null && givenLast is not null)
        {
            firstName = givenFirst;
            lastName = givenLast;
            return;
        }

        if (givenFirst is not null)
        {
            firstName = givenFirst;
            lastName = givenLast ?? "User";
            return;
        }

        if (!string.IsNullOrEmpty(localPart))
        {
            var name = localPart.Replace('.', ' ').Replace('_', ' ').Replace('-', ' ');
            var parts = name.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            firstName = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(parts[0]);
            lastName = parts.Length > 1
                ? CultureInfo.CurrentCulture.TextInfo.ToTitleCase(string.Join(" ", parts.Skip(1)))
                : "User";
            return;
        }

        firstName = "eShopOnWeb";
        lastName = "Subscriber";
    }

    private static SubscriptionPurchase MapSubscription(MaxioSubscription s, bool alreadySubscribed)
    {
        return new SubscriptionPurchase
        {
            SubscriptionId = s.Id,
            State = s.State ?? string.Empty,
            PlanHandle = s.Product?.Handle ?? string.Empty,
            PlanName = s.Product?.Name ?? string.Empty,
            ProductPriceInCents = s.ProductPriceInCents ?? s.Product?.PriceInCents ?? 0,
            Currency = s.Currency ?? "USD",
            CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
            NextBillingAt = s.NextAssessmentAt,
            ActivatedAt = s.ActivatedAt,
            CreatedAt = s.CreatedAt ?? DateTimeOffset.UtcNow,
            BalanceInCents = s.BalanceInCents ?? 0,
            AlreadySubscribed = alreadySubscribed
        };
    }
}
