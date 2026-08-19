using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "trialing", "assessing", "pending", "past_due",
        "unpaid", "soft_failure", "paused", "awaiting_signup"
    };

    private readonly MaxioApiClient _maxio;
    private readonly MaxioOptions _options;
    private readonly SubscriptionEnrollmentGate _enrollmentGate;
    private readonly IAppLogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        MaxioApiClient maxio,
        IOptions<MaxioOptions> options,
        SubscriptionEnrollmentGate enrollmentGate,
        IAppLogger<MaxioSubscriptionBillingService> logger)
    {
        _maxio = maxio;
        _options = options.Value;
        _enrollmentGate = enrollmentGate;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var products = await _maxio.ListProductsForFamilyAsync(_options.ProductFamilyHandle, cancellationToken);
        return products
            .Where(p => !string.IsNullOrWhiteSpace(p.Handle))
            .Select(MapPlan)
            .ToList();
    }

    public async Task<CustomerSubscription> SubscribeAsync(
        ShopperIdentity shopper,
        string productHandle,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(shopper);
        EnsureConfigured();

        var handle = productHandle?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(handle))
        {
            throw new BillingValidationException("A productHandle is required.");
        }

        var gateKey = $"{shopper.UserId}:{handle}";
        return await _enrollmentGate.RunAsync(gateKey, () => EnrollAsync(shopper, handle, cancellationToken));
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListMySubscriptionsAsync(
        ShopperIdentity shopper,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(shopper);
        EnsureConfigured();

        var customer = await _maxio.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(MapSubscription).ToList();
    }

    private async Task<CustomerSubscription> EnrollAsync(
        ShopperIdentity shopper,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var plans = await ListPlansAsync(cancellationToken);
        if (plans.All(p => !string.Equals(p.Handle, productHandle, StringComparison.Ordinal)))
        {
            throw new BillingValidationException(
                $"Plan '{productHandle}' is not available in product family '{_options.ProductFamilyHandle}'.");
        }

        var customer = await EnsureCustomerAsync(shopper, cancellationToken);
        var subscriptionReference = BuildSubscriptionReference(shopper.UserId, productHandle);

        var existingByReference = await _maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
        if (existingByReference is not null && IsLive(existingByReference.State))
        {
            _logger.LogInformation("Returning existing Maxio subscription {SubscriptionId} for shopper {UserId}.", existingByReference.Id, shopper.UserId);
            return MapSubscription(existingByReference);
        }

        var customerSubscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var liveMatch = customerSubscriptions.FirstOrDefault(s =>
            IsLive(s.State) &&
            string.Equals(s.Product?.Handle, productHandle, StringComparison.Ordinal));
        if (liveMatch is not null)
        {
            return MapSubscription(liveMatch);
        }

        try
        {
            var created = await _maxio.CreateSubscriptionAsync(
                new MaxioCreateSubscription
                {
                    ProductHandle = productHandle,
                    CustomerId = customer.Id,
                    Reference = existingByReference is null ? subscriptionReference : $"{subscriptionReference}:{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}"
                },
                cancellationToken);

            _logger.LogInformation("Created Maxio subscription {SubscriptionId} for shopper {UserId}.", created.Id, shopper.UserId);
            return MapSubscription(created);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var raced = await _maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
            if (raced is not null)
            {
                return MapSubscription(raced);
            }

            throw;
        }
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(
        ShopperIdentity shopper,
        CancellationToken cancellationToken)
    {
        var existing = await _maxio.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = SplitName(shopper);
        try
        {
            var created = await _maxio.CreateCustomerAsync(
                new MaxioCreateCustomer
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = shopper.Email,
                    Reference = shopper.UserId
                },
                cancellationToken);

            _logger.LogInformation("Created Maxio customer {CustomerId} for shopper {UserId}.", created.Id, shopper.UserId);
            return created;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var raced = await _maxio.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw;
        }
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException("Maxio:ApiKey is not configured.");
        }

        if (string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw new InvalidOperationException("Maxio:ProductFamilyHandle is not configured.");
        }

        if (string.IsNullOrWhiteSpace(_options.BaseUrl) && string.IsNullOrWhiteSpace(_options.Subdomain))
        {
            throw new InvalidOperationException("Maxio:Subdomain or Maxio:BaseUrl is required.");
        }
    }

    internal static string BuildSubscriptionReference(string userId, string productHandle) =>
        $"{userId}:{productHandle}";

    internal static (string FirstName, string LastName) SplitName(ShopperIdentity shopper)
    {
        var source = string.IsNullOrWhiteSpace(shopper.DisplayName)
            ? shopper.Email
            : shopper.DisplayName;

        var local = source.Contains('@', StringComparison.Ordinal)
            ? source.Split('@')[0]
            : source;

        var parts = local.Split(new[] { '.', ' ', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        var first = parts.Length > 0 ? parts[0] : "Shopper";
        return (first, "eShopOnWeb");
    }

    private static bool IsLive(string? state) =>
        !string.IsNullOrWhiteSpace(state) && LiveStates.Contains(state);

    private static SubscriptionPlan MapPlan(MaxioProduct product) => new()
    {
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? product.Handle ?? string.Empty,
        Description = product.Description,
        Price = MaxioMoney.FromCents(product.PriceInCents),
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? string.Empty
    };

    private static CustomerSubscription MapSubscription(MaxioSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State ?? string.Empty,
        PlanHandle = subscription.Product?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? subscription.Product?.Handle ?? string.Empty,
        Price = MaxioMoney.FromCents(subscription.ProductPriceInCents != 0
            ? subscription.ProductPriceInCents
            : subscription.Product?.PriceInCents),
        NextBillingAt = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt,
        CreatedAt = subscription.CreatedAt
    };
}
