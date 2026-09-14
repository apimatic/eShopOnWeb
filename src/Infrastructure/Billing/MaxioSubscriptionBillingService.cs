using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> EnrollmentLocks = new();
    private static readonly TimeSpan ReservationLifetime = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ReservationWait = TimeSpan.FromSeconds(35);

    private readonly IMaxioClient _maxio;
    private readonly CatalogContext _catalogContext;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        IMaxioClient maxio,
        CatalogContext catalogContext,
        IOptions<MaxioOptions> options,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _maxio = maxio;
        _catalogContext = catalogContext;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(
        CancellationToken cancellationToken = default)
    {
        var products = await _maxio.ListProductsForFamilyAsync(
            _options.ProductFamilyHandle,
            cancellationToken);

        return products
            .Where(x => x.ArchivedAt is null && !string.IsNullOrWhiteSpace(x.Handle))
            .OrderBy(x => x.PriceInCents)
            .Select(ToPlan)
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(
        SubscriptionShopper shopper,
        string productHandle,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productHandle);

        var plan = (await ListPlansAsync(cancellationToken))
            .SingleOrDefault(x => string.Equals(x.Handle, productHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new SubscriptionPlanNotFoundException(productHandle);
        }

        var customerReference = BuildReference("customer", shopper.UserId);
        var subscriptionReference = BuildReference(
            "subscription",
            $"{shopper.UserId}|{_options.ProductFamilyHandle}|{plan.Handle}");
        var enrollmentLock = EnrollmentLocks.GetOrAdd(subscriptionReference, _ => new SemaphoreSlim(1, 1));

        await enrollmentLock.WaitAsync(cancellationToken);
        try
        {
            var existing = await _maxio.FindSubscriptionByReferenceAsync(
                subscriptionReference,
                cancellationToken);
            if (existing is not null)
            {
                await SaveMappingAsync(
                    shopper.UserId,
                    plan.Handle,
                    existing.Customer.Id,
                    existing.Id,
                    customerReference,
                    subscriptionReference,
                    cancellationToken);
                return new SubscribeResult(ToSubscription(existing), false);
            }

            if (!await TryAcquireReservationAsync(
                    shopper.UserId,
                    plan.Handle,
                    customerReference,
                    subscriptionReference,
                    cancellationToken))
            {
                var concurrentSubscription = await WaitForConcurrentEnrollmentAsync(
                    shopper.UserId,
                    plan.Handle,
                    subscriptionReference,
                    cancellationToken);
                return new SubscribeResult(ToSubscription(concurrentSubscription), false);
            }

            var customer = await EnsureCustomerAsync(shopper, customerReference, cancellationToken);
            MaxioSubscription subscription;
            try
            {
                subscription = await _maxio.CreateSubscriptionAsync(
                    new MaxioCreateSubscription
                    {
                        ProductHandle = plan.Handle,
                        CustomerId = customer.Id,
                        PaymentCollectionMethod = "remittance",
                        Reference = subscriptionReference
                    },
                    cancellationToken);
            }
            catch (BillingProviderException createException)
            {
                _logger.LogWarning(createException, "Maxio subscription creation had an ambiguous result; recovering by reference.");
                // A timeout or duplicate-reference response can occur after Maxio committed the
                // enrollment. The reference lookup makes that ambiguous outcome recoverable.
                subscription = await _maxio.FindSubscriptionByReferenceAsync(
                        subscriptionReference,
                        CancellationToken.None)
                    ?? throw new BillingProviderException(
                        "Maxio did not confirm the subscription enrollment.",
                        createException);
            }

            await SaveMappingAsync(
                shopper.UserId,
                plan.Handle,
                customer.Id,
                subscription.Id,
                customerReference,
                subscriptionReference,
                cancellationToken);

            return new SubscribeResult(ToSubscription(subscription), true);
        }
        finally
        {
            enrollmentLock.Release();
        }
    }

    public async Task<IReadOnlyList<ShopperSubscription>> ListSubscriptionsAsync(
        SubscriptionShopper shopper,
        CancellationToken cancellationToken = default)
    {
        var customerReference = BuildReference("customer", shopper.UserId);
        var customer = await _maxio.FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<ShopperSubscription>();
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions
            .Where(x => string.Equals(
                x.Product.ProductFamily?.Handle,
                _options.ProductFamilyHandle,
                StringComparison.OrdinalIgnoreCase))
            .Select(ToSubscription)
            .ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(
        SubscriptionShopper shopper,
        string customerReference,
        CancellationToken cancellationToken)
    {
        var customer = await _maxio.FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (customer is not null)
        {
            return customer;
        }

        var (firstName, lastName) = ResolveNames(shopper);
        try
        {
            return await _maxio.CreateCustomerAsync(
                new MaxioCreateCustomer
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = shopper.Email,
                    Reference = customerReference
                },
                cancellationToken);
        }
        catch (BillingProviderException createException)
        {
            _logger.LogWarning(createException, "Maxio customer creation had an ambiguous result; recovering by reference.");
            return await _maxio.FindCustomerByReferenceAsync(customerReference, CancellationToken.None)
                ?? throw new BillingProviderException(
                    "Maxio did not confirm the customer creation.",
                    createException);
        }
    }

    private async Task SaveMappingAsync(
        string userId,
        string productHandle,
        long maxioCustomerId,
        long maxioSubscriptionId,
        string customerReference,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        var record = await _catalogContext.UserSubscriptions.SingleOrDefaultAsync(
            x => x.UserId == userId && x.ProductHandle == productHandle,
            cancellationToken);

        if (record is null)
        {
            record = new UserSubscription(
                userId,
                productHandle,
                customerReference,
                subscriptionReference,
                DateTime.UtcNow,
                Guid.NewGuid().ToString());
            _catalogContext.UserSubscriptions.Add(record);
        }

        record.Complete(maxioCustomerId, maxioSubscriptionId, DateTime.UtcNow);

        try
        {
            await _catalogContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            // Maxio is the system of record. A concurrent local unique-key winner already stores
            // the same deterministic mapping, so persistence contention must not duplicate or
            // invalidate a successful provider enrollment.
            _logger.LogWarning(exception, "A concurrent request already persisted subscription {SubscriptionId}.", maxioSubscriptionId);
        }
    }

    private async Task<bool> TryAcquireReservationAsync(
        string userId,
        string productHandle,
        string customerReference,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var token = Guid.NewGuid().ToString();
        var record = await _catalogContext.UserSubscriptions.SingleOrDefaultAsync(
            x => x.UserId == userId && x.ProductHandle == productHandle,
            cancellationToken);

        if (record is null)
        {
            _catalogContext.UserSubscriptions.Add(new UserSubscription(
                userId,
                productHandle,
                customerReference,
                subscriptionReference,
                now,
                token));
        }
        else
        {
            if (record.MaxioSubscriptionId.HasValue || record.UpdatedAtUtc > now - ReservationLifetime)
            {
                return false;
            }

            record.RenewReservation(token, now);
        }

        try
        {
            await _catalogContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            _catalogContext.ChangeTracker.Clear();
            return false;
        }
    }

    private async Task<MaxioSubscription> WaitForConcurrentEnrollmentAsync(
        string userId,
        string productHandle,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + ReservationWait;
        while (DateTime.UtcNow < deadline)
        {
            _catalogContext.ChangeTracker.Clear();
            var record = await _catalogContext.UserSubscriptions.AsNoTracking().SingleOrDefaultAsync(
                x => x.UserId == userId && x.ProductHandle == productHandle,
                cancellationToken);
            if (record?.MaxioSubscriptionId is not null)
            {
                var completed = await _maxio.FindSubscriptionByReferenceAsync(
                    subscriptionReference,
                    cancellationToken);
                if (completed is not null)
                {
                    return completed;
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }

        var recovered = await _maxio.FindSubscriptionByReferenceAsync(
            subscriptionReference,
            cancellationToken);
        return recovered ?? throw new BillingProviderException(
            "A subscription enrollment for this plan is already in progress.");
    }

    private static SubscriptionPlan ToPlan(MaxioProduct product) => new(
        product.Id,
        product.Handle!,
        product.Name,
        product.Description,
        product.PriceInCents,
        product.Interval,
        product.IntervalUnit);

    private static ShopperSubscription ToSubscription(MaxioSubscription subscription) => new(
        subscription.Id,
        subscription.Product.Handle ?? string.Empty,
        subscription.Product.Name,
        subscription.ProductPriceInCents,
        subscription.Currency,
        subscription.State,
        subscription.CurrentPeriodEndsAt);

    private static string BuildReference(string kind, string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return $"eshop-{kind}-{Convert.ToHexString(hash)[..32].ToLowerInvariant()}";
    }

    private static (string FirstName, string LastName) ResolveNames(SubscriptionShopper shopper)
    {
        if (!string.IsNullOrWhiteSpace(shopper.FirstName) && !string.IsNullOrWhiteSpace(shopper.LastName))
        {
            return (shopper.FirstName.Trim(), shopper.LastName.Trim());
        }

        var localPart = shopper.Email.Split('@', 2)[0];
        var parts = localPart.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        var firstName = !string.IsNullOrWhiteSpace(shopper.FirstName)
            ? shopper.FirstName.Trim()
            : Humanize(parts.FirstOrDefault() ?? "eShop");
        var lastName = !string.IsNullOrWhiteSpace(shopper.LastName)
            ? shopper.LastName.Trim()
            : parts.Length > 1 ? Humanize(parts[^1]) : "Customer";
        return (firstName, lastName);
    }

    private static string Humanize(string value) => value.Length switch
    {
        0 => value,
        1 => value.ToUpperInvariant(),
        _ => char.ToUpperInvariant(value[0]) + value[1..]
    };
}
