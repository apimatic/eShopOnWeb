using System.Collections.Concurrent;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active",
        "trialing",
        "past_due",
        "assessing",
        "pending",
        "soft_failure",
        "unpaid",
        "paused",
        "on_hold",
        "suspended"
    };

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscribeGates = new(StringComparer.Ordinal);

    private readonly IMaxioAdvancedBillingClient _maxio;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        IMaxioAdvancedBillingClient maxio,
        IOptions<MaxioOptions> options,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _maxio = maxio;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var products = await _maxio.ListProductsForFamilyAsync(_options.ProductFamilyHandle, cancellationToken);
        return products
            .Where(product => !string.IsNullOrWhiteSpace(product.Handle))
            .Select(ToPlan)
            .ToList();
    }

    public async Task<ShopperSubscription> SubscribeAsync(
        ShopperIdentity shopper,
        string productHandle,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        ArgumentNullException.ThrowIfNull(shopper);

        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new BillingValidationException("A product handle is required to subscribe.");
        }

        var handle = productHandle.Trim();
        var plans = await ListPlansAsync(cancellationToken);
        if (!plans.Any(plan => string.Equals(plan.Handle, handle, StringComparison.OrdinalIgnoreCase)))
        {
            throw new SubscriptionPlanNotFoundException(handle);
        }

        var gate = SubscribeGates.GetOrAdd($"{shopper.UserId}:{handle}", _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(shopper, cancellationToken);
            var existing = await FindLiveSubscriptionAsync(customer.Id, handle, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation(
                    "Returning existing Maxio subscription {SubscriptionId} for shopper {ShopperId} on {ProductHandle}",
                    existing.Id, shopper.UserId, handle);
                return ToShopperSubscription(existing);
            }

            var uniquenessToken = $"eshop-subscribe:{shopper.UserId}:{handle}:remittance";
            try
            {
                var created = await _maxio.CreateSubscriptionAsync(handle, customer.Id, uniquenessToken, cancellationToken);
                _logger.LogInformation(
                    "Created Maxio subscription {SubscriptionId} for shopper {ShopperId} on {ProductHandle} in state {State}",
                    created.Id, shopper.UserId, handle, created.State);
                return ToShopperSubscription(created);
            }
            catch (MaxioDuplicateSubmissionException)
            {
                var duplicate = await FindLiveSubscriptionAsync(customer.Id, handle, cancellationToken);
                if (duplicate is not null)
                {
                    return ToShopperSubscription(duplicate);
                }

                throw new BillingValidationException(
                    "A subscribe request for this plan was already submitted. Retry shortly.");
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<ShopperSubscription>> ListShopperSubscriptionsAsync(
        ShopperIdentity shopper,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        ArgumentNullException.ThrowIfNull(shopper);

        var customer = await _maxio.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<ShopperSubscription>();
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(ToShopperSubscription).ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(ShopperIdentity shopper, CancellationToken cancellationToken)
    {
        var existing = await _maxio.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = SplitName(shopper.Email, shopper.UserName);
        try
        {
            return await _maxio.CreateCustomerAsync(new MaxioCreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = shopper.Email,
                Reference = shopper.UserId
            }, cancellationToken);
        }
        catch (BillingValidationException)
        {
            var raced = await _maxio.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
            if (raced is not null)
            {
                return raced;
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
        return subscriptions.FirstOrDefault(subscription =>
            IsLive(subscription.State) &&
            string.Equals(subscription.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase));
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new BillingProviderException("Maxio:ApiKey is not configured.");
        }

        if (string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw new BillingProviderException("Maxio:ProductFamilyHandle is not configured.");
        }

        if (string.IsNullOrWhiteSpace(_options.BaseUrl) && string.IsNullOrWhiteSpace(_options.Subdomain))
        {
            throw new BillingProviderException("Set Maxio:BaseUrl or Maxio:Subdomain.");
        }
    }

    internal static SubscriptionPlan ToPlan(MaxioProduct product)
    {
        return new SubscriptionPlan(
            product.Handle ?? string.Empty,
            product.Name ?? product.Handle ?? "Plan",
            product.Description,
            CentsToAmount(product.PriceInCents),
            product.Interval,
            product.IntervalUnit ?? "month",
            product.ProductPricePointName);
    }

    internal static ShopperSubscription ToShopperSubscription(MaxioSubscription subscription)
    {
        var priceCents = subscription.ProductPriceInCents != 0
            ? subscription.ProductPriceInCents
            : subscription.Product?.PriceInCents ?? 0;

        return new ShopperSubscription(
            subscription.Id,
            subscription.Product?.Handle ?? string.Empty,
            subscription.Product?.Name ?? subscription.Product?.Handle ?? "Plan",
            CentsToAmount(priceCents),
            subscription.State ?? "unknown",
            subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt);
    }

    internal static decimal CentsToAmount(long cents) => cents / 100m;

    internal static (string FirstName, string LastName) SplitName(string email, string? userName)
    {
        var source = !string.IsNullOrWhiteSpace(email) ? email : userName ?? "Shopper";
        var local = source.Contains('@') ? source[..source.IndexOf('@')] : source;
        var parts = local.Split(new[] { '.', '_', '-' }, 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            return (Capitalize(parts[0]), Capitalize(parts[1]));
        }

        return (Capitalize(local), "Subscriber");
    }

    private static string Capitalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Shopper";
        }

        var trimmed = value.Trim();
        return char.ToUpperInvariant(trimmed[0]) + (trimmed.Length > 1 ? trimmed[1..] : string.Empty);
    }

    private static bool IsLive(string? state) =>
        !string.IsNullOrWhiteSpace(state) && LiveStates.Contains(state);
}
