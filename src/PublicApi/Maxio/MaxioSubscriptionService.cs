using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Orchestrates subscription operations against Maxio Advanced Billing, which is the
/// billing system of record. Customer identity is bridged via the Maxio customer
/// <c>reference</c> field, which stores the eShopOnWeb user id — this keeps the mapping
/// authoritative in Maxio (idempotent per user) instead of in a local database.
/// </summary>
public class MaxioSubscriptionService : ISubscriptionService
{
    private static readonly Regex NameTokenSeparator = new("[._\\-]+", RegexOptions.Compiled);

    private readonly IMaxioClient _maxioClient;
    private readonly MaxioOptions _options;

    public MaxioSubscriptionService(IMaxioClient maxioClient, IOptions<MaxioOptions> options)
    {
        _maxioClient = maxioClient;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _maxioClient.ListProductsInFamilyAsync(_options.ProductFamilyHandle, cancellationToken);
        return products
            .Where(p => p.ArchivedAt == null && !string.IsNullOrEmpty(p.Handle))
            .Select(MapPlan)
            .ToList();
    }

    public async Task<SubscriptionDto> SubscribeAsync(string userId, string email, string productHandle, CancellationToken cancellationToken = default)
    {
        var plans = await GetPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, productHandle, StringComparison.OrdinalIgnoreCase));
        if (plan == null)
        {
            throw new MaxioApiException((int)HttpStatusCode.NotFound, string.Empty,
                $"Subscription plan '{productHandle}' was not found in product family '{_options.ProductFamilyHandle}'.");
        }

        var customer = await EnsureCustomerAsync(userId, email, cancellationToken);

        // Idempotency: never create a second live subscription for the same user + plan.
        // The lookup goes through Maxio (system of record), so it holds across restarts
        // even when the local datastore is empty.
        var existing = await FindLiveSubscriptionAsync(customer.Id, plan.Handle!, cancellationToken);
        if (existing != null)
        {
            return MapSubscription(existing, alreadySubscribed: true);
        }

        var created = await _maxioClient.CreateSubscriptionAsync(plan.Handle!, customer.Id, cancellationToken);
        return MapSubscription(created, alreadySubscribed: false);
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(string userId, string email, CancellationToken cancellationToken = default)
    {
        var customer = await _maxioClient.GetCustomerByReferenceAsync(userId, cancellationToken);
        if (customer == null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var subscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(s => MapSubscription(s, alreadySubscribed: true)).ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(string userId, string email, CancellationToken cancellationToken)
    {
        var existing = await _maxioClient.GetCustomerByReferenceAsync(userId, cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        var (firstName, lastName) = DeriveCustomerName(email);

        try
        {
            return await _maxioClient.CreateCustomerAsync(firstName, lastName, email, userId, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == (int)HttpStatusCode.UnprocessableEntity)
        {
            // Maxio enforces unique customer references. A concurrent request may have won
            // the race; re-look-up so a double-click never surfaces as an error.
            var raced = await _maxioClient.GetCustomerByReferenceAsync(userId, cancellationToken);
            if (raced != null)
            {
                return raced;
            }

            throw;
        }
    }

    private async Task<MaxioSubscription?> FindLiveSubscriptionAsync(int customerId, string productHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            MaxioSubscriptionStates.LiveStates.Contains(s.State) &&
            string.Equals(s.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase));
    }

    private static (string FirstName, string LastName) DeriveCustomerName(string email)
    {
        var localPart = email.Contains('@') ? email[..email.IndexOf('@')] : email;
        var tokens = NameTokenSeparator.Split(localPart).Where(t => t.Length > 0).ToList();
        if (tokens.Count == 0)
        {
            return ("eShopOnWeb", "Customer");
        }

        var firstName = Capitalize(tokens[0]);
        var lastName = tokens.Count > 1 ? Capitalize(string.Join(" ", tokens.Skip(1))) : "Customer";
        return (firstName, lastName);
    }

    private static string Capitalize(string value) =>
        char.ToUpperInvariant(value[0]) + value[1..];

    private static SubscriptionPlanDto MapPlan(MaxioProduct product) =>
        new()
        {
            Handle = product.Handle!,
            Name = product.Name,
            Description = product.Description,
            Price = product.PriceInCents / 100m,
            PriceInCents = product.PriceInCents,
            BillingInterval = product.Interval,
            BillingIntervalUnit = product.IntervalUnit,
            TrialInterval = product.TrialInterval,
            TrialIntervalUnit = product.TrialIntervalUnit,
            RequiresPaymentMethod = product.RequireCreditCard,
            Taxable = product.Taxable
        };

    private static SubscriptionDto MapSubscription(MaxioSubscription subscription, bool alreadySubscribed)
    {
        var product = subscription.Product;
        return new SubscriptionDto
        {
            SubscriptionId = subscription.Id,
            State = subscription.State,
            PlanHandle = product?.Handle ?? string.Empty,
            PlanName = product?.Name ?? string.Empty,
            Price = subscription.ProductPriceInCents / 100m,
            PriceInCents = subscription.ProductPriceInCents,
            BillingInterval = product?.Interval ?? 0,
            BillingIntervalUnit = product?.IntervalUnit ?? string.Empty,
            NextBillingAt = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            ActivatedAt = subscription.ActivatedAt,
            CreatedAt = subscription.CreatedAt,
            CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod,
            AlreadySubscribed = alreadySubscribed
        };
    }
}
