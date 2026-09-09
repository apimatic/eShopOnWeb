using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Implements the eShopOnWeb subscription capability against Maxio Advanced Billing. Orchestrates the
/// hero "Subscribe" flow idempotently: it ensures a Maxio customer exists for the shopper (keyed by a stable
/// reference) and avoids creating a second subscription when an equivalent one already exists.
/// </summary>
public class MaxioSubscriptionService : ISubscriptionService
{
    /// <summary>
    /// Both demo plans are configured with "payment method not required", so subscriptions are created with
    /// remittance (invoice) collection — this activates the subscription without capturing a card / 3-DS.
    /// </summary>
    private const string RemittanceCollection = "remittance";

    /// <summary>
    /// Subscription states considered terminated for the purpose of de-duplication. A shopper who already has
    /// a non-terminated subscription to a plan is not enrolled again.
    /// </summary>
    private static readonly HashSet<string> TerminatedStates =
        new(StringComparer.OrdinalIgnoreCase) { "canceled", "cancelled", "expired" };

    private readonly IMaxioClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        IMaxioClient client,
        MaxioSettings settings,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _settings = settings;
        _logger = logger;
    }

    public async Task<IReadOnlyCollection<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var products = await ListPlansInternalAsync(cancellationToken);
        return products
            .Select(ToPlan)
            .OrderBy(plan => plan.PriceInCents)
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(
        SubscribeCommand command, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        if (command is null) throw new ArgumentNullException(nameof(command));
        if (string.IsNullOrWhiteSpace(command.PlanHandle))
        {
            throw new SubscriptionPlanNotFoundException(command.PlanHandle ?? string.Empty);
        }
        if (string.IsNullOrWhiteSpace(command.ShopperReference))
        {
            throw new ArgumentException("A shopper reference is required to subscribe.", nameof(command));
        }

        // 1. Validate the requested plan exists in the configured family (gives a clean 404 instead of a 422 later).
        var plans = await ListPlansInternalAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p =>
            string.Equals(p.Handle, command.PlanHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new SubscriptionPlanNotFoundException(command.PlanHandle);
        }

        // 2. Ensure a Maxio customer exists for the shopper (idempotent by reference).
        var (customer, customerCreated) = await EnsureCustomerAsync(command, cancellationToken);

        // 3. If the shopper already has a live subscription to this plan, return it instead of duplicating.
        var existing = await FindActiveSubscriptionForPlanAsync(customer.Id, plan.Handle!, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation(
                "Shopper {Reference} already has active subscription {SubscriptionId} on plan {Plan}; not creating a duplicate.",
                command.ShopperReference, existing.Id, plan.Handle);
            return new SubscribeResult
            {
                Subscription = ToShopperSubscription(existing),
                CustomerId = customer.Id,
                AlreadyExisted = true,
                CustomerCreated = customerCreated,
            };
        }

        // 4. Create the subscription.
        MaxioSubscription created;
        try
        {
            created = await _client.CreateSubscriptionAsync(
                new CreateSubscriptionBody(plan.Handle!, customer.Id, RemittanceCollection),
                cancellationToken);
        }
        catch (MaxioApiException ex)
        {
            throw new SubscriptionBillingException(
                $"Could not create the subscription: {string.Join("; ", ex.Errors)}", ex);
        }

        _logger.LogInformation(
            "Created subscription {SubscriptionId} ({State}) for shopper {Reference} on plan {Plan}.",
            created.Id, created.State, command.ShopperReference, plan.Handle);

        return new SubscribeResult
        {
            Subscription = ToShopperSubscription(created),
            CustomerId = customer.Id,
            AlreadyExisted = false,
            CustomerCreated = customerCreated,
        };
    }

    public async Task<IReadOnlyCollection<ShopperSubscription>> GetSubscriptionsAsync(
        string shopperReference, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        if (string.IsNullOrWhiteSpace(shopperReference))
        {
            return Array.Empty<ShopperSubscription>();
        }

        var customer = await FindCustomerAsync(shopperReference, cancellationToken);
        if (customer is null)
        {
            // No customer yet means the shopper has never subscribed.
            return Array.Empty<ShopperSubscription>();
        }

        var subscriptions = await ListSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions
            .Select(ToShopperSubscription)
            .OrderByDescending(s => s.CreatedAt ?? DateTimeOffset.MinValue)
            .ToList();
    }

    /// <summary>Verifies the integration is configured, surfacing a clear billing error otherwise.</summary>
    private void EnsureConfigured()
    {
        try
        {
            _settings.Validate();
        }
        catch (InvalidOperationException ex)
        {
            throw new SubscriptionBillingException(ex.Message, ex);
        }
    }

    private async Task<IReadOnlyList<MaxioProduct>> ListPlansInternalAsync(CancellationToken cancellationToken)
    {
        try
        {
            var products = await _client.ListProductsForFamilyAsync(_settings.ProductFamilyHandle, cancellationToken);
            // Only products with a stable handle can be subscribed to.
            return products.Where(p => !string.IsNullOrWhiteSpace(p.Handle)).ToList();
        }
        catch (MaxioApiException ex)
        {
            throw new SubscriptionBillingException(
                $"Could not load subscription plans from Maxio: {string.Join("; ", ex.Errors)}", ex);
        }
    }

    private async Task<(MaxioCustomer Customer, bool Created)> EnsureCustomerAsync(
        SubscribeCommand command, CancellationToken cancellationToken)
    {
        var existing = await FindCustomerAsync(command.ShopperReference, cancellationToken);
        if (existing is not null)
        {
            return (existing, false);
        }

        var (firstName, lastName) = ResolveName(command);
        try
        {
            var created = await _client.CreateCustomerAsync(
                new CreateCustomerBody(firstName, lastName, command.Email, command.ShopperReference),
                cancellationToken);
            return (created, true);
        }
        catch (MaxioApiException ex)
        {
            // Handle the double-click race: a concurrent request may have just created the customer, and the
            // `reference` must be unique. Re-look it up before giving up so we never create two customers.
            var raced = await FindCustomerAsync(command.ShopperReference, cancellationToken);
            if (raced is not null)
            {
                return (raced, false);
            }

            throw new SubscriptionBillingException(
                $"Could not create a billing customer: {string.Join("; ", ex.Errors)}", ex);
        }
    }

    private async Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            return await _client.FindCustomerByReferenceAsync(reference, cancellationToken);
        }
        catch (MaxioApiException ex)
        {
            throw new SubscriptionBillingException(
                $"Could not look up the billing customer: {string.Join("; ", ex.Errors)}", ex);
        }
    }

    private async Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsAsync(
        int customerId, CancellationToken cancellationToken)
    {
        try
        {
            return await _client.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        }
        catch (MaxioApiException ex)
        {
            throw new SubscriptionBillingException(
                $"Could not list the shopper's subscriptions: {string.Join("; ", ex.Errors)}", ex);
        }
    }

    private async Task<MaxioSubscription?> FindActiveSubscriptionForPlanAsync(
        int customerId, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await ListSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase)
            && !TerminatedStates.Contains(s.State));
    }

    private static (string FirstName, string LastName) ResolveName(SubscribeCommand command)
    {
        var firstName = string.IsNullOrWhiteSpace(command.FirstName) ? "eShopOnWeb" : command.FirstName.Trim();
        var lastName = string.IsNullOrWhiteSpace(command.LastName) ? "Shopper" : command.LastName.Trim();
        return (firstName, lastName);
    }

    private SubscriptionPlan ToPlan(MaxioProduct product) => new()
    {
        Handle = product.Handle ?? string.Empty,
        ProductId = product.Id,
        Name = product.Name,
        Description = product.Description,
        PriceInCents = (int)product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit,
        ProductFamilyHandle = product.ProductFamily?.Handle ?? _settings.ProductFamilyHandle,
    };

    private static ShopperSubscription ToShopperSubscription(MaxioSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        PlanHandle = subscription.Product?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? string.Empty,
        PriceInCents = (int)subscription.ProductPriceInCents,
        PaymentCollectionMethod = subscription.PaymentCollectionMethod ?? string.Empty,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingAt = subscription.NextAssessmentAt,
        CreatedAt = subscription.CreatedAt,
    };
}
