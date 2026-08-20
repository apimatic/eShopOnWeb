using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly HashSet<string> LiveSubscriptionStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "pending",
        "trialing",
        "assessing",
        "active",
        "soft_failure",
        "past_due",
        "paused",
        "unpaid"
    };

    private readonly IMaxioAdvancedBillingClient _maxio;
    private readonly IOptions<MaxioOptions> _options;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _subscriberLocks = new();

    public MaxioSubscriptionBillingService(
        IMaxioAdvancedBillingClient maxio,
        IOptions<MaxioOptions> options,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _maxio = maxio;
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default)
    {
        var familyHandle = RequireProductFamilyHandle();
        var products = await ListAllProductsInFamilyAsync(familyHandle, cancellationToken);

        return products
            .Where(p => string.IsNullOrEmpty(p.ArchivedAt) && !string.IsNullOrWhiteSpace(p.Handle))
            .Select(ToPlan)
            .OrderBy(p => p.Price)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(
        Subscriber subscriber,
        string productHandle,
        CancellationToken cancellationToken = default)
    {
        if (subscriber == null) throw new ArgumentNullException(nameof(subscriber));
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new BillingException("A product handle is required to subscribe.", 400);
        }

        productHandle = productHandle.Trim();
        var gate = _subscriberLocks.GetOrAdd(subscriber.UserId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await SubscribeCoreAsync(subscriber, productHandle, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<ShopperSubscription>> GetSubscriptionsForUserAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Array.Empty<ShopperSubscription>();
        }

        var customer = await _maxio.ReadCustomerByReferenceAsync(userId, cancellationToken);
        if (customer == null)
        {
            return Array.Empty<ShopperSubscription>();
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(ToShopperSubscription).ToList();
    }

    private async Task<SubscribeResult> SubscribeCoreAsync(
        Subscriber subscriber,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var plans = await GetAvailablePlansAsync(cancellationToken);
        if (plans.All(p => !string.Equals(p.Handle, productHandle, StringComparison.OrdinalIgnoreCase)))
        {
            throw new SubscriptionPlanNotFoundException(productHandle);
        }

        var subscriptionReference = BuildSubscriptionReference(subscriber.UserId, productHandle);
        var existingByReference = await _maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
        if (existingByReference != null)
        {
            _logger.LogInformation("Reusing existing Maxio subscription {SubscriptionId} for user {UserId} plan {Plan}.",
                existingByReference.Id, subscriber.UserId, productHandle);
            return new SubscribeResult(ToShopperSubscription(existingByReference), created: false);
        }

        var customer = await EnsureCustomerAsync(subscriber, cancellationToken);

        var existingForCustomer = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var liveMatch = existingForCustomer.FirstOrDefault(s =>
            IsLive(s.State) &&
            string.Equals(s.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase));
        if (liveMatch != null)
        {
            _logger.LogInformation("Reusing live Maxio subscription {SubscriptionId} for user {UserId} plan {Plan}.",
                liveMatch.Id, subscriber.UserId, productHandle);
            return new SubscribeResult(ToShopperSubscription(liveMatch), created: false);
        }

        try
        {
            var created = await _maxio.CreateSubscriptionAsync(new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscriptionDto
                {
                    ProductHandle = productHandle,
                    CustomerId = customer.Id,
                    PaymentCollectionMethod = "remittance",
                    Reference = subscriptionReference
                }
            }, cancellationToken);

            _logger.LogInformation("Created Maxio subscription {SubscriptionId} for user {UserId} plan {Plan}.",
                created.Id, subscriber.UserId, productHandle);
            return new SubscribeResult(ToShopperSubscription(created), created: true);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var raced = await _maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
            if (raced != null)
            {
                return new SubscribeResult(ToShopperSubscription(raced), created: false);
            }

            var summary = MaxioApiException.TryReadErrorSummary(ex.ResponseBody);
            throw new BillingException(
                string.IsNullOrWhiteSpace(summary) ? "Maxio rejected the subscription request." : summary,
                ex,
                400);
        }
    }

    private async Task<CustomerDto> EnsureCustomerAsync(Subscriber subscriber, CancellationToken cancellationToken)
    {
        var existing = await _maxio.ReadCustomerByReferenceAsync(subscriber.UserId, cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        var (firstName, lastName) = SplitName(subscriber);
        try
        {
            return await _maxio.CreateCustomerAsync(new CreateCustomerRequest
            {
                Customer = new CreateCustomerDto
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = subscriber.Email,
                    Reference = subscriber.UserId
                }
            }, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var raced = await _maxio.ReadCustomerByReferenceAsync(subscriber.UserId, cancellationToken);
            if (raced != null)
            {
                return raced;
            }

            var summary = MaxioApiException.TryReadErrorSummary(ex.ResponseBody);
            throw new BillingException(
                string.IsNullOrWhiteSpace(summary) ? "Maxio rejected the customer request." : summary,
                ex,
                400);
        }
    }

    private async Task<List<ProductDto>> ListAllProductsInFamilyAsync(string familyHandle, CancellationToken cancellationToken)
    {
        var all = new List<ProductDto>();
        const int perPage = 200;
        var page = 1;
        while (true)
        {
            var batch = await _maxio.ListProductsForProductFamilyAsync(familyHandle, page, perPage, cancellationToken);
            all.AddRange(batch);
            if (batch.Count < perPage)
            {
                break;
            }

            page++;
        }

        return all;
    }

    private string RequireProductFamilyHandle()
    {
        var handle = _options.Value.ProductFamilyHandle;
        if (string.IsNullOrWhiteSpace(handle))
        {
            throw new BillingException("Maxio:ProductFamilyHandle is not configured.", 500);
        }

        return handle.Trim();
    }

    internal static string BuildSubscriptionReference(string userId, string productHandle)
        => $"{userId}:{productHandle}";

    internal static (string FirstName, string LastName) SplitName(Subscriber subscriber)
    {
        var source = subscriber.UserName;
        var at = source.IndexOf('@');
        if (at > 0)
        {
            source = source[..at];
        }

        var parts = source.Split(new[] { '.', '-', '_', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var first = parts.Length > 0 ? Capitalize(parts[0]) : "Shopper";
        var last = parts.Length > 1 ? Capitalize(string.Join(" ", parts.Skip(1))) : "eShopOnWeb";
        return (first, last);
    }

    private static string Capitalize(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return char.ToUpper(value[0], CultureInfo.InvariantCulture) + value[1..];
    }

    private static bool IsLive(string? state)
        => !string.IsNullOrWhiteSpace(state) && LiveSubscriptionStates.Contains(state);

    private static SubscriptionPlan ToPlan(ProductDto product)
    {
        return new SubscriptionPlan
        {
            Handle = product.Handle ?? string.Empty,
            Name = product.Name ?? product.Handle ?? string.Empty,
            Description = product.Description,
            Price = MaxioMoney.CentsToAmount(product.PriceInCents),
            Interval = product.Interval,
            IntervalUnit = product.IntervalUnit ?? "month"
        };
    }

    internal static ShopperSubscription ToShopperSubscription(SubscriptionDto subscription)
    {
        var nextBilling = ParseTimestamp(subscription.NextAssessmentAt)
                          ?? ParseTimestamp(subscription.CurrentPeriodEndsAt);

        return new ShopperSubscription
        {
            Id = subscription.Id,
            ProductHandle = subscription.Product?.Handle ?? string.Empty,
            ProductName = subscription.Product?.Name ?? subscription.Product?.Handle ?? string.Empty,
            Price = MaxioMoney.CentsToAmount(subscription.ProductPriceInCents),
            State = subscription.State ?? string.Empty,
            NextBillingDate = nextBilling
        };
    }

    private static DateTimeOffset? ParseTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            return parsed;
        }

        return null;
    }
}
