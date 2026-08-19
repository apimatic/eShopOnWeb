using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly HashSet<string> EndOfLifeStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled",
        "expired",
        "failed_to_create",
        "on_hold",
        "suspended",
        "trial_ended"
    };

    private readonly MaxioAdvancedBillingClient _maxio;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _buyerLocks = new();

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient maxio,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _maxio = maxio;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _maxio.ListProductsForFamilyAsync(_maxio.ProductFamilyHandle, cancellationToken);
        return products
            .Where(product => product.ArchivedAt is null && !string.IsNullOrWhiteSpace(product.Handle))
            .Select(MapPlan)
            .ToList();
    }

    public async Task<ShopperSubscription> SubscribeAsync(
        SubscribeShopperRequest request,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(request.BuyerId, nameof(request.BuyerId));
        Guard.Against.NullOrEmpty(request.Email, nameof(request.Email));
        Guard.Against.NullOrEmpty(request.ProductHandle, nameof(request.ProductHandle));

        var gate = _buyerLocks.GetOrAdd(request.BuyerId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var subscription = await SubscribeCoreAsync(request, cancellationToken);
            _logger.LogInformation(
                "Shopper {BuyerId} is subscribed to {ProductHandle} as Maxio subscription {SubscriptionId} (existing={AlreadyExisted}).",
                request.BuyerId,
                request.ProductHandle,
                subscription.Id,
                subscription.AlreadyExisted);
            return subscription;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<ShopperSubscription>> ListSubscriptionsForBuyerAsync(
        string buyerId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var customer = await _maxio.LookupCustomerByReferenceAsync(buyerId, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<ShopperSubscription>();
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(item => MapSubscription(item, alreadyExisted: true)).ToList();
    }

    private async Task<ShopperSubscription> SubscribeCoreAsync(
        SubscribeShopperRequest request,
        CancellationToken cancellationToken)
    {
        var subscriptionReference = BuildSubscriptionReference(request.BuyerId, request.ProductHandle);
        var existing = await FindLiveSubscriptionAsync(request, subscriptionReference, cancellationToken);
        if (existing is not null)
        {
            return MapSubscription(existing, alreadyExisted: true);
        }

        await EnsurePlanExistsAsync(request.ProductHandle, cancellationToken);
        var customer = await EnsureCustomerAsync(request, cancellationToken);

        try
        {
            var created = await _maxio.CreateSubscriptionAsync(new MaxioCreateSubscriptionRequest
            {
                UniquenessToken = Guid.NewGuid().ToString(),
                Subscription = new MaxioCreateSubscriptionAttributes
                {
                    ProductHandle = request.ProductHandle,
                    CustomerId = customer.Id,
                    Reference = subscriptionReference,
                    PaymentCollectionMethod = "remittance"
                }
            }, cancellationToken);

            return MapSubscription(created, alreadyExisted: false);
        }
        catch (MaxioDuplicateSubmissionException)
        {
            var recovered = await RecoverSubscriptionAsync(request, subscriptionReference, cancellationToken);
            return MapSubscription(recovered, alreadyExisted: true);
        }
        catch (MaxioApiException ex) when (IsReferenceConflict(ex))
        {
            var recovered = await RecoverSubscriptionAsync(request, subscriptionReference, cancellationToken);
            return MapSubscription(recovered, alreadyExisted: true);
        }
    }

    private async Task<MaxioSubscription?> FindLiveSubscriptionAsync(
        SubscribeShopperRequest request,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        var byReference = await _maxio.LookupSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
        if (IsLive(byReference))
        {
            return byReference;
        }

        var customer = await _maxio.LookupCustomerByReferenceAsync(request.BuyerId, cancellationToken);
        if (customer is null)
        {
            return null;
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.FirstOrDefault(item =>
            IsLive(item) &&
            string.Equals(item.Product?.Handle, request.ProductHandle, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(
        SubscribeShopperRequest request,
        CancellationToken cancellationToken)
    {
        var existing = await _maxio.LookupCustomerByReferenceAsync(request.BuyerId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = SplitName(request.UserName, request.Email);
        try
        {
            return await _maxio.CreateCustomerAsync(new MaxioCreateCustomerRequest
            {
                UniquenessToken = $"eshop-customer-{request.BuyerId}",
                Customer = new MaxioCreateCustomerAttributes
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = request.Email,
                    Reference = request.BuyerId
                }
            }, cancellationToken);
        }
        catch (MaxioDuplicateSubmissionException)
        {
            return await RequireCustomerAsync(request.BuyerId, cancellationToken);
        }
        catch (MaxioApiException ex) when (IsReferenceConflict(ex) || ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            return await RequireCustomerAsync(request.BuyerId, cancellationToken);
        }
    }

    private async Task<MaxioCustomer> RequireCustomerAsync(string buyerId, CancellationToken cancellationToken)
    {
        var customer = await _maxio.LookupCustomerByReferenceAsync(buyerId, cancellationToken);
        if (customer is null)
        {
            throw new MaxioApiException(
                "A Maxio customer could not be found after a conflicting create.",
                HttpStatusCode.Conflict);
        }

        return customer;
    }

    private async Task<MaxioSubscription> RecoverSubscriptionAsync(
        SubscribeShopperRequest request,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var recovered = await FindLiveSubscriptionAsync(request, subscriptionReference, cancellationToken);
            if (recovered is not null)
            {
                return recovered;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200 * (attempt + 1)), cancellationToken);
        }

        throw new MaxioApiException(
            "A duplicate subscribe request was detected but the existing subscription could not be loaded.",
            HttpStatusCode.Conflict);
    }

    private async Task EnsurePlanExistsAsync(string productHandle, CancellationToken cancellationToken)
    {
        var plans = await ListPlansAsync(cancellationToken);
        if (!plans.Any(plan => string.Equals(plan.Handle, productHandle, StringComparison.OrdinalIgnoreCase)))
        {
            throw new MaxioApiException(
                $"Subscription plan '{productHandle}' was not found in the configured Maxio product family.",
                HttpStatusCode.NotFound);
        }
    }

    private static bool IsLive(MaxioSubscription? subscription)
        => subscription is not null &&
           !string.IsNullOrWhiteSpace(subscription.State) &&
           !EndOfLifeStates.Contains(subscription.State);

    private static bool IsReferenceConflict(MaxioApiException ex)
    {
        var body = $"{ex.Message} {ex.ResponseBody}";
        return body.Contains("reference", StringComparison.OrdinalIgnoreCase)
               && (body.Contains("taken", StringComparison.OrdinalIgnoreCase)
                   || body.Contains("unique", StringComparison.OrdinalIgnoreCase)
                   || body.Contains("already", StringComparison.OrdinalIgnoreCase));
    }

    internal static string BuildSubscriptionReference(string buyerId, string productHandle)
        => $"{buyerId}:{productHandle}";

    internal static (string FirstName, string LastName) SplitName(string? userName, string email)
    {
        var source = !string.IsNullOrWhiteSpace(userName) ? userName : email;
        var local = source.Split('@')[0];
        var parts = local.Split(new[] { '.', '-', '_', '+' }, StringSplitOptions.RemoveEmptyEntries);
        var first = parts.Length > 0 ? Capitalize(parts[0]) : "Shopper";
        var last = parts.Length > 1 ? Capitalize(parts[1]) : "Customer";
        return (first, last);
    }

    private static string Capitalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return char.ToUpper(value[0], CultureInfo.InvariantCulture) + value[1..].ToLowerInvariant();
    }

    private static SubscriptionPlan MapPlan(MaxioProduct product) => new()
    {
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? product.Handle ?? string.Empty,
        Description = product.Description,
        Price = CentsToCurrency(product.PriceInCents),
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? string.Empty,
        RequiresPaymentMethod = product.RequireCreditCard
    };

    private static ShopperSubscription MapSubscription(MaxioSubscription subscription, bool alreadyExisted) => new()
    {
        Id = subscription.Id,
        State = subscription.State ?? string.Empty,
        ProductHandle = subscription.Product?.Handle ?? string.Empty,
        ProductName = subscription.Product?.Name ?? subscription.Product?.Handle ?? string.Empty,
        Price = CentsToCurrency(subscription.ProductPriceInCents != 0
            ? subscription.ProductPriceInCents
            : subscription.Product?.PriceInCents ?? 0),
        NextBillingAt = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        Reference = subscription.Reference,
        AlreadyExisted = alreadyExisted
    };

    private static decimal CentsToCurrency(long cents) => cents / 100m;
}
