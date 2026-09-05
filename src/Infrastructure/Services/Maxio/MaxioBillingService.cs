using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Billing;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Maxio;

internal sealed class MaxioBillingService : IMaxioBillingService
{
    // Subscription states that mean "this subscription is no longer occupying the buyer's slot
    // for this plan", so a repeat subscribe should be allowed to create a fresh one. Every other
    // state Maxio documents (active, trialing, past_due, unpaid, suspended, on_hold,
    // awaiting_signup, ...) is treated as "already subscribed" for idempotency purposes.
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled",
        "expired",
        "trial_ended"
    };

    private readonly IMaxioApiClient _client;
    private readonly MaxioBuyerLock _buyerLock;
    private readonly MaxioSettings _settings;

    public MaxioBillingService(IMaxioApiClient client, MaxioBuyerLock buyerLock, IOptions<MaxioSettings> settings)
    {
        _client = client;
        _buyerLock = buyerLock;
        _settings = settings.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.ProductFamilyHandle))
        {
            throw new InvalidOperationException("Maxio:ProductFamilyHandle is not configured.");
        }

        var products = await _client.ListProductsForFamilyAsync(_settings.ProductFamilyHandle, cancellationToken);
        return products
            .Where(p => p.ArchivedAt is null)
            .Select(MapPlan)
            .ToList();
    }

    public async Task<SubscriberSubscription> SubscribeAsync(SubscriptionEnrollmentRequest request, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(request.BuyerReference, nameof(request.BuyerReference));
        Guard.Against.NullOrWhiteSpace(request.Email, nameof(request.Email));
        Guard.Against.NullOrWhiteSpace(request.PlanHandle, nameof(request.PlanHandle));

        // See MaxioBuyerLock: Maxio has no idempotency-key primitive for subscription creation,
        // so a per-buyer lock plus the check-for-an-existing-subscription-first logic below is
        // what makes a UI double-click safe.
        var buyerLock = _buyerLock.For(request.BuyerReference);
        await buyerLock.WaitAsync(cancellationToken);
        try
        {
            var plan = await _client.FindProductByHandleAsync(request.PlanHandle, cancellationToken)
                ?? throw new MaxioApiException(404, $"No plan with handle '{request.PlanHandle}' was found.");

            var customer = await FindOrCreateCustomerAsync(request.BuyerReference, request.Email, cancellationToken);

            var existing = await FindNonTerminalSubscriptionAsync(customer.Id, request.PlanHandle, cancellationToken);
            if (existing is not null)
            {
                return MapSubscription(existing, customer.Id);
            }

            var created = await _client.CreateSubscriptionAsync(new CreateSubscriptionAttributes
            {
                ProductHandle = request.PlanHandle,
                CustomerId = customer.Id,
                // A plan configured with no required payment method still defaults to attempting
                // an automatic card charge on signup, which fails with a 422 when there is no
                // card on file. "remittance" defers payment collection instead of blocking
                // subscription creation on having a stored payment method.
                PaymentCollectionMethod = plan.RequireCreditCard ? null : "remittance"
            }, cancellationToken);

            return MapSubscription(created, customer.Id);
        }
        finally
        {
            buyerLock.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriberSubscription>> GetSubscriptionsForBuyerAsync(string buyerReference, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(buyerReference, nameof(buyerReference));

        var customer = await _client.FindCustomerByReferenceAsync(buyerReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriberSubscription>();
        }

        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(s => MapSubscription(s, customer.Id)).ToList();
    }

    private async Task<MaxioCustomer> FindOrCreateCustomerAsync(string buyerReference, string email, CancellationToken cancellationToken)
    {
        var existing = await _client.FindCustomerByReferenceAsync(buyerReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = ResolveNameFromEmail(email);

        try
        {
            return await _client.CreateCustomerAsync(new CreateCustomerAttributes
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = buyerReference
            }, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 422)
        {
            // Maxio enforces a unique `reference` per customer and rejects a repeat with 422 -
            // that is our confirmation the customer already exists (e.g. a racing request beat us
            // to it), so go fetch it instead of failing the subscribe attempt.
            var afterConflict = await _client.FindCustomerByReferenceAsync(buyerReference, cancellationToken);
            if (afterConflict is not null)
            {
                return afterConflict;
            }

            throw;
        }
    }

    private async Task<MaxioSubscription?> FindNonTerminalSubscriptionAsync(int customerId, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase) &&
            !TerminalStates.Contains(s.State));
    }

    private static (string FirstName, string LastName) ResolveNameFromEmail(string email)
    {
        // eShopOnWeb's Identity model (ApplicationUser : IdentityUser) never captures a display
        // name, only email/username - so this derives a best-effort name for Maxio's required
        // first_name/last_name fields from the email's local part.
        var localPart = email.Split('@')[0];
        var tokens = localPart.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);

        static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

        return tokens.Length >= 2
            ? (Capitalize(tokens[0]), Capitalize(tokens[1]))
            : (Capitalize(localPart), "Customer");
    }

    private static SubscriptionPlan MapPlan(MaxioProduct product) => new(
        product.Handle,
        product.Name,
        product.Description,
        product.PriceInCents / 100m,
        product.Interval,
        product.IntervalUnit,
        product.RequireCreditCard);

    private static SubscriberSubscription MapSubscription(MaxioSubscription subscription, int customerId) => new(
        subscription.Id,
        customerId,
        subscription.Product?.Handle ?? string.Empty,
        subscription.Product?.Name ?? string.Empty,
        (subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? 0) / 100m,
        subscription.State,
        subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt);
}
