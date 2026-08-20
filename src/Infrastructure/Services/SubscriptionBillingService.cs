using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class SubscriptionBillingService : ISubscriptionBillingService
{
    private readonly IMaxioBillingClient _maxio;
    private readonly MaxioOptions _options;
    private readonly SubscriptionEnrollmentGate _enrollmentGate;
    private readonly ILogger<SubscriptionBillingService> _logger;

    public SubscriptionBillingService(
        IMaxioBillingClient maxio,
        IOptions<MaxioOptions> options,
        SubscriptionEnrollmentGate enrollmentGate,
        ILogger<SubscriptionBillingService> logger)
    {
        _maxio = maxio;
        _options = options.Value;
        _enrollmentGate = enrollmentGate;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var familyHandle = RequireProductFamilyHandle();
        var products = await _maxio.ListProductsForFamilyAsync(familyHandle, cancellationToken);
        return products
            .OrderBy(product => product.PriceInCents)
            .Select(MapPlan)
            .ToList();
    }

    public Task<ShopperSubscription> SubscribeAsync(
        ShopperIdentity shopper,
        string productHandle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shopper);
        ArgumentException.ThrowIfNullOrWhiteSpace(shopper.UserId);

        var handle = (productHandle ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(handle))
        {
            throw new MaxioBillingException(
                HttpStatusCode.BadRequest,
                "productHandle is required.");
        }

        var gateKey = $"{shopper.UserId}:{handle}";
        return _enrollmentGate.RunAsync(gateKey, () => SubscribeCoreAsync(shopper, handle, cancellationToken), cancellationToken);
    }

    public async Task<IReadOnlyList<ShopperSubscription>> ListShopperSubscriptionsAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var customer = await _maxio.FindCustomerByReferenceAsync(userId, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<ShopperSubscription>();
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions
            .OrderByDescending(subscription => subscription.CreatedAt)
            .Select(MapSubscription)
            .ToList();
    }

    private async Task<ShopperSubscription> SubscribeCoreAsync(
        ShopperIdentity shopper,
        string productHandle,
        CancellationToken cancellationToken)
    {
        await EnsurePlanExistsAsync(productHandle, cancellationToken);

        var customer = await EnsureCustomerAsync(shopper, cancellationToken);

        var existing = await FindLiveSubscriptionAsync(customer.Id, shopper.UserId, productHandle, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation(
                "Returning existing Maxio subscription {SubscriptionId} for user {UserId} on plan {ProductHandle}.",
                existing.Id,
                shopper.UserId,
                productHandle);
            return MapSubscription(existing);
        }

        var subscriptionReference = await AllocateSubscriptionReferenceAsync(
            shopper.UserId, productHandle, cancellationToken);

        try
        {
            var created = await _maxio.CreateSubscriptionAsync(
                new BillingSubscriptionDraft
                {
                    ProductHandle = productHandle,
                    CustomerId = customer.Id,
                    Reference = subscriptionReference
                },
                uniquenessToken: Guid.NewGuid().ToString("N"),
                cancellationToken);

            return MapSubscription(created);
        }
        catch (MaxioDuplicateSubmissionException)
        {
            var recovered = await FindLiveSubscriptionAsync(customer.Id, shopper.UserId, productHandle, cancellationToken);
            if (recovered is not null)
            {
                return MapSubscription(recovered);
            }

            throw;
        }
        catch (MaxioBillingException ex) when ((int)ex.StatusCode == 422)
        {
            var recovered = await FindLiveSubscriptionAsync(customer.Id, shopper.UserId, productHandle, cancellationToken);
            if (recovered is not null)
            {
                return MapSubscription(recovered);
            }

            throw;
        }
    }

    private async Task EnsurePlanExistsAsync(string productHandle, CancellationToken cancellationToken)
    {
        var plans = await _maxio.ListProductsForFamilyAsync(RequireProductFamilyHandle(), cancellationToken);
        if (!plans.Any(plan => string.Equals(plan.Handle, productHandle, StringComparison.OrdinalIgnoreCase)))
        {
            throw new MaxioBillingException(
                HttpStatusCode.NotFound,
                $"Unknown subscription plan '{productHandle}'.");
        }
    }

    private async Task<BillingCustomer> EnsureCustomerAsync(ShopperIdentity shopper, CancellationToken cancellationToken)
    {
        var existing = await _maxio.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            return await _maxio.CreateCustomerAsync(
                new BillingCustomerDraft
                {
                    FirstName = string.IsNullOrWhiteSpace(shopper.FirstName) ? "Shopper" : shopper.FirstName,
                    LastName = string.IsNullOrWhiteSpace(shopper.LastName) ? "eShopOnWeb" : shopper.LastName,
                    Email = shopper.Email,
                    Reference = shopper.UserId
                },
                uniquenessToken: $"eshop-customer:{shopper.UserId}:{Guid.NewGuid():N}",
                cancellationToken);
        }
        catch (MaxioDuplicateSubmissionException)
        {
            return await RequireCustomerByReferenceAsync(shopper.UserId, cancellationToken);
        }
        catch (MaxioBillingException ex) when ((int)ex.StatusCode == 422)
        {
            return await RequireCustomerByReferenceAsync(shopper.UserId, cancellationToken);
        }
    }

    private async Task<BillingCustomer> RequireCustomerByReferenceAsync(string userId, CancellationToken cancellationToken)
    {
        var customer = await _maxio.FindCustomerByReferenceAsync(userId, cancellationToken);
        if (customer is null)
        {
            throw new MaxioBillingException(
                HttpStatusCode.BadGateway,
                "Maxio reported a customer conflict but the customer could not be loaded by reference.");
        }

        return customer;
    }

    private async Task<string> AllocateSubscriptionReferenceAsync(
        string userId,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var canonical = BuildSubscriptionReference(userId, productHandle);
        var existing = await _maxio.FindSubscriptionByReferenceAsync(canonical, cancellationToken);
        if (existing is null)
        {
            return canonical;
        }

        return $"{canonical}:{Guid.NewGuid():N}";
    }

    private async Task<BillingSubscription?> FindLiveSubscriptionAsync(
        int customerId,
        string userId,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var byReference = await _maxio.FindSubscriptionByReferenceAsync(
            BuildSubscriptionReference(userId, productHandle), cancellationToken);
        if (byReference is not null && SubscriptionStates.IsLive(byReference.State))
        {
            return byReference;
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(subscription =>
            SubscriptionStates.IsLive(subscription.State)
            && string.Equals(subscription.ProductHandle, productHandle, StringComparison.OrdinalIgnoreCase));
    }

    private string RequireProductFamilyHandle()
    {
        if (string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw new MaxioBillingException(
                HttpStatusCode.ServiceUnavailable,
                "Maxio is not configured. Bind Maxio:ProductFamilyHandle.");
        }

        return _options.ProductFamilyHandle.Trim();
    }

    private static string BuildSubscriptionReference(string userId, string productHandle) =>
        $"eshop:{userId}:{productHandle}";

    private static SubscriptionPlan MapPlan(BillingProduct product) => new()
    {
        Handle = product.Handle,
        Name = product.Name,
        Description = product.Description,
        Price = BillingMoney.FromCents(product.PriceInCents),
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit
    };

    private static ShopperSubscription MapSubscription(BillingSubscription subscription) => new()
    {
        Id = subscription.Id,
        ProductHandle = subscription.ProductHandle ?? string.Empty,
        ProductName = subscription.ProductName ?? string.Empty,
        Price = BillingMoney.FromCents(subscription.ProductPriceInCents),
        State = subscription.State,
        NextBillingDate = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
        CreatedAt = subscription.CreatedAt
    };
}
