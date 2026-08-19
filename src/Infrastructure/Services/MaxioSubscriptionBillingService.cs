using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionBilling;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "pending",
        "trialing",
        "assessing",
        "active",
        "soft_failure",
        "past_due",
        "unpaid",
        "paused",
        "awaiting_signup"
    };

    private readonly IMaxioAdvancedBillingClient _maxio;
    private readonly MaxioOptions _options;
    private readonly SubscriptionCreationGate _gate;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        IMaxioAdvancedBillingClient maxio,
        IOptions<MaxioOptions> options,
        SubscriptionCreationGate gate,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _maxio = maxio;
        _options = options.Value;
        _gate = gate;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        EnsureProductFamilyConfigured();
        var products = await _maxio.ListProductsForFamilyAsync(_options.ProductFamilyHandle, cancellationToken);
        return products
            .Where(p => p.ArchivedAt is null && !string.IsNullOrWhiteSpace(p.Handle))
            .Select(MapPlan)
            .ToList();
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListMySubscriptionsAsync(
        ShopperIdentity shopper,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shopper);

        var customer = await _maxio.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(MapSubscription).ToList();
    }

    public Task<CustomerSubscription> SubscribeAsync(
        ShopperIdentity shopper,
        string? productHandle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shopper);
        if (string.IsNullOrWhiteSpace(shopper.UserId))
        {
            throw new ArgumentException("Shopper user id is required.", nameof(shopper));
        }

        return _gate.RunAsync(
            shopper.UserId,
            () => SubscribeCoreAsync(shopper, productHandle, cancellationToken),
            cancellationToken);
    }

    private async Task<CustomerSubscription> SubscribeCoreAsync(
        ShopperIdentity shopper,
        string? productHandle,
        CancellationToken cancellationToken)
    {
        var plan = await ResolvePlanAsync(productHandle, cancellationToken);
        var customer = await EnsureCustomerAsync(shopper, cancellationToken);

        var existing = await FindLiveSubscriptionAsync(customer.Id, plan.Handle, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation(
                "Shopper {UserId} already has live Maxio subscription {SubscriptionId} on {Handle}; returning existing.",
                shopper.UserId, existing.Id, plan.Handle);
            return MapSubscription(existing);
        }

        var uniquenessToken = Guid.NewGuid().ToString("D");
        var request = new MaxioCreateSubscriptionRequest
        {
            UniquenessToken = uniquenessToken,
            Subscription = new MaxioCreateSubscription
            {
                ProductHandle = plan.Handle,
                CustomerId = customer.Id,
                Reference = $"{shopper.UserId}:{plan.Handle}",
                // Invoice/remittance collection lets signup succeed when the product
                // does not require a stored card (no 3-DS / card capture).
                PaymentCollectionMethod = "remittance"
            }
        };

        try
        {
            var created = await _maxio.CreateSubscriptionAsync(request, cancellationToken);
            _logger.LogInformation(
                "Created Maxio subscription {SubscriptionId} for shopper {UserId} on {Handle}.",
                created.Id, shopper.UserId, plan.Handle);
            return MapSubscription(created);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            // uniqueness_token replay (double-submit / retry after a timeout).
            var replayed = await FindLiveSubscriptionAsync(customer.Id, plan.Handle, cancellationToken);
            if (replayed is not null)
            {
                return MapSubscription(replayed);
            }

            throw;
        }
        catch (MaxioApiException ex) when ((int)ex.StatusCode == 422 && LooksLikeLegacyInvoiceSite(ex.Message))
        {
            request.UniquenessToken = Guid.NewGuid().ToString("D");
            request.Subscription.PaymentCollectionMethod = "invoice";
            var created = await _maxio.CreateSubscriptionAsync(request, cancellationToken);
            return MapSubscription(created);
        }
        catch (MaxioApiException ex) when ((int)ex.StatusCode == 422)
        {
            // Customer already subscribed or a race created the subscription first.
            var raced = await FindLiveSubscriptionAsync(customer.Id, plan.Handle, cancellationToken);
            if (raced is not null)
            {
                return MapSubscription(raced);
            }

            throw;
        }
    }

    private async Task<SubscriptionPlan> ResolvePlanAsync(string? productHandle, CancellationToken cancellationToken)
    {
        var plans = await ListPlansAsync(cancellationToken);
        if (plans.Count == 0)
        {
            throw new MaxioConfigurationException(
                $"No products were found in Maxio product family '{_options.ProductFamilyHandle}'.");
        }

        if (string.IsNullOrWhiteSpace(productHandle))
        {
            return plans[0];
        }

        var match = plans.FirstOrDefault(p =>
            string.Equals(p.Handle, productHandle, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            throw new SubscriptionPlanNotFoundException(productHandle);
        }

        return match;
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(ShopperIdentity shopper, CancellationToken cancellationToken)
    {
        var existing = await _maxio.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = SplitName(shopper);
        var request = new MaxioCreateCustomerRequest
        {
            UniquenessToken = $"customer:{shopper.UserId}",
            Customer = new MaxioCreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = shopper.Email,
                Reference = shopper.UserId,
                Organization = "eShopOnWeb"
            }
        };

        try
        {
            var created = await _maxio.CreateCustomerAsync(request, cancellationToken);
            _logger.LogInformation("Created Maxio customer {CustomerId} for shopper {UserId}.", created.Id, shopper.UserId);
            return created;
        }
        catch (MaxioApiException ex) when (
            ex.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity)
        {
            var afterRace = await _maxio.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
            if (afterRace is not null)
            {
                return afterRace;
            }

            throw;
        }
    }

    private async Task<MaxioSubscription?> FindLiveSubscriptionAsync(
        int customerId,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            IsLive(s.State) &&
            string.Equals(s.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase));
    }

    private void EnsureProductFamilyConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw new MaxioConfigurationException(
                "Maxio:ProductFamilyHandle is not configured. Set MAXIO_DEFAULT_PRODUCT_FAMILY.");
        }
    }

    private static bool IsLive(string? state) =>
        !string.IsNullOrWhiteSpace(state) && LiveStates.Contains(state);

    private static bool LooksLikeLegacyInvoiceSite(string message) =>
        message.Contains("payment_collection_method", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("not a valid collection method", StringComparison.OrdinalIgnoreCase);

    private static SubscriptionPlan MapPlan(MaxioProduct product) => new()
    {
        Handle = product.Handle!,
        Name = product.Name ?? product.Handle!,
        Description = product.Description,
        Price = CentsToDecimal(product.PriceInCents),
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? "month",
        RequiresPaymentMethod = product.RequireCreditCard,
        ProductFamilyHandle = product.ProductFamily?.Handle
    };

    private static CustomerSubscription MapSubscription(MaxioSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State ?? "unknown",
        ProductHandle = subscription.Product?.Handle,
        ProductName = subscription.Product?.Name,
        Price = CentsToDecimal(
            subscription.ProductPriceInCents != 0
                ? subscription.ProductPriceInCents
                : subscription.Product?.PriceInCents ?? 0),
        NextBillingAt = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CreatedAt = subscription.CreatedAt
    };

    private static decimal CentsToDecimal(long cents) => cents / 100m;

    private static (string FirstName, string LastName) SplitName(ShopperIdentity shopper)
    {
        var source = shopper.UserName;
        if (string.IsNullOrWhiteSpace(source))
        {
            source = shopper.Email;
        }

        var local = source.Split('@')[0];
        var token = string.IsNullOrWhiteSpace(local) ? "Shopper" : local;
        return (token, "eShopOnWeb");
    }
}
