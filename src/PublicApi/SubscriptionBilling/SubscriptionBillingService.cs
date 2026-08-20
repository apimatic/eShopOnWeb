using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionBilling;

public sealed class SubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly TimeSpan ReservationLease = TimeSpan.FromMinutes(2);
    private readonly CatalogContext _dbContext;
    private readonly IMaxioClient _maxioClient;
    private readonly ISubscriptionOperationLock _operationLock;
    private readonly MaxioOptions _options;

    public SubscriptionBillingService(
        CatalogContext dbContext,
        IMaxioClient maxioClient,
        ISubscriptionOperationLock operationLock,
        IOptions<MaxioOptions> options)
    {
        _dbContext = dbContext;
        _maxioClient = maxioClient;
        _operationLock = operationLock;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var siteTask = _maxioClient.GetSiteAsync(cancellationToken);
        var productsTask = _maxioClient.ListProductsAsync(cancellationToken);
        await Task.WhenAll(siteTask, productsTask);

        return productsTask.Result
            .Where(IsAvailablePlan)
            .OrderBy(product => product.PriceInCents)
            .Select(product => MapPlan(product, siteTask.Result.Currency))
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(Shopper shopper, string productHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new SubscriptionBillingException((int)HttpStatusCode.BadRequest, "Invalid plan", "A productHandle is required.");
        }

        var products = await _maxioClient.ListProductsAsync(cancellationToken);
        var product = products.SingleOrDefault(candidate =>
            IsAvailablePlan(candidate) &&
            string.Equals(candidate.Handle, productHandle, StringComparison.OrdinalIgnoreCase));

        if (product is null)
        {
            throw new SubscriptionBillingException((int)HttpStatusCode.NotFound, "Plan not found", "The requested subscription plan is not available.");
        }

        if (product.RequireCreditCard)
        {
            throw new SubscriptionBillingException(
                (int)HttpStatusCode.UnprocessableEntity,
                "Payment method required",
                "This plan requires a payment method and cannot be enrolled through this no-card subscription flow.");
        }

        var normalizedHandle = product.Handle!;
        var lockKey = $"{shopper.UserId}:{normalizedHandle}";
        using var operationLease = await _operationLock.AcquireAsync(lockKey, cancellationToken);

        var subscriptionReference = BuildSubscriptionReference(shopper.UserId, normalizedHandle);
        var existingSubscription = await _maxioClient.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
        if (existingSubscription is not null)
        {
            await ReconcileMappingsAsync(shopper.UserId, existingSubscription.Customer, existingSubscription, normalizedHandle, cancellationToken);
            return new SubscribeResult(MapSubscription(existingSubscription), false);
        }

        var reservation = await ReserveEnrollmentAsync(shopper.UserId, normalizedHandle, subscriptionReference, cancellationToken);
        var customer = await EnsureCustomerAsync(shopper, cancellationToken);

        try
        {
            var subscription = await _maxioClient.CreateSubscriptionAsync(
                new MaxioCreateSubscription
                {
                    ProductHandle = normalizedHandle,
                    CustomerId = customer.Id,
                    Reference = subscriptionReference,
                    PaymentCollectionMethod = "remittance"
                },
                cancellationToken);

            reservation.Complete(subscription.Id);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return new SubscribeResult(MapSubscription(subscription), true);
        }
        catch (MaxioApiException exception) when (exception.StatusCode == (int)HttpStatusCode.UnprocessableEntity)
        {
            var racedSubscription = await _maxioClient.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
            if (racedSubscription is not null)
            {
                reservation.Complete(racedSubscription.Id);
                await _dbContext.SaveChangesAsync(cancellationToken);
                return new SubscribeResult(MapSubscription(racedSubscription), false);
            }

            _dbContext.SubscriptionEnrollments.Remove(reservation);
            await _dbContext.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    public async Task<IReadOnlyList<ShopperSubscription>> ListSubscriptionsAsync(Shopper shopper, CancellationToken cancellationToken)
    {
        var customerReference = BuildCustomerReference(shopper.UserId);
        var customer = await _maxioClient.FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<ShopperSubscription>();
        }

        await ReconcileCustomerMappingAsync(shopper.UserId, customer, cancellationToken);
        var subscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions
            .Where(subscription => string.Equals(
                subscription.Product.ProductFamily.Handle,
                _options.ProductFamilyHandle,
                StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(subscription => subscription.Id)
            .Select(MapSubscription)
            .ToList();
    }

    private bool IsAvailablePlan(MaxioProduct product)
    {
        return product.ArchivedAt is null &&
            !string.IsNullOrWhiteSpace(product.Handle) &&
            string.Equals(product.ProductFamily.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(Shopper shopper, CancellationToken cancellationToken)
    {
        var reference = BuildCustomerReference(shopper.UserId);
        var customer = await _maxioClient.FindCustomerByReferenceAsync(reference, cancellationToken);

        if (customer is null)
        {
            var (firstName, lastName) = DeriveNames(shopper.Email);
            try
            {
                customer = await _maxioClient.CreateCustomerAsync(
                    new MaxioCreateCustomer
                    {
                        FirstName = firstName,
                        LastName = lastName,
                        Email = shopper.Email,
                        Reference = reference
                    },
                    cancellationToken);
            }
            catch (MaxioApiException exception) when (exception.StatusCode == (int)HttpStatusCode.UnprocessableEntity)
            {
                customer = await _maxioClient.FindCustomerByReferenceAsync(reference, cancellationToken);
                if (customer is null)
                {
                    throw;
                }
            }
        }

        await ReconcileCustomerMappingAsync(shopper.UserId, customer, cancellationToken);
        return customer;
    }

    private async Task<SubscriptionEnrollment> ReserveEnrollmentAsync(
        string userId,
        string productHandle,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        var existing = await _dbContext.SubscriptionEnrollments
            .SingleOrDefaultAsync(enrollment => enrollment.UserId == userId && enrollment.ProductHandle == productHandle, cancellationToken);

        if (existing is null)
        {
            var reservation = new SubscriptionEnrollment(userId, productHandle, subscriptionReference);
            _dbContext.SubscriptionEnrollments.Add(reservation);
            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                return reservation;
            }
            catch (DbUpdateException)
            {
                _dbContext.Entry(reservation).State = EntityState.Detached;
                existing = await _dbContext.SubscriptionEnrollments
                    .SingleAsync(enrollment => enrollment.UserId == userId && enrollment.ProductHandle == productHandle, cancellationToken);
            }
        }

        if (existing.UpdatedAtUtc > DateTimeOffset.UtcNow.Subtract(ReservationLease))
        {
            throw new SubscriptionBillingException(
                (int)HttpStatusCode.Conflict,
                "Enrollment in progress",
                "An enrollment for this plan is already in progress. Retry shortly to receive the completed subscription.");
        }

        existing.RenewReservation();
        await _dbContext.SaveChangesAsync(cancellationToken);
        return existing;
    }

    private async Task ReconcileMappingsAsync(
        string userId,
        MaxioCustomer customer,
        MaxioSubscription subscription,
        string productHandle,
        CancellationToken cancellationToken)
    {
        await ReconcileCustomerMappingAsync(userId, customer, cancellationToken);
        var enrollment = await _dbContext.SubscriptionEnrollments
            .SingleOrDefaultAsync(item => item.UserId == userId && item.ProductHandle == productHandle, cancellationToken);
        if (enrollment is null)
        {
            enrollment = new SubscriptionEnrollment(userId, productHandle, subscription.Reference ?? BuildSubscriptionReference(userId, productHandle));
            _dbContext.SubscriptionEnrollments.Add(enrollment);
        }

        enrollment.Complete(subscription.Id);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task ReconcileCustomerMappingAsync(string userId, MaxioCustomer customer, CancellationToken cancellationToken)
    {
        var mapping = await _dbContext.BillingCustomers.SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        if (mapping is null)
        {
            _dbContext.BillingCustomers.Add(new BillingCustomer(
                userId,
                customer.Id,
                customer.Reference ?? BuildCustomerReference(userId)));
        }
        else
        {
            mapping.Reconcile(customer.Id);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static SubscriptionPlan MapPlan(MaxioProduct product, string currency)
    {
        return new SubscriptionPlan(
            product.Handle!,
            product.Name,
            product.Description,
            product.PriceInCents,
            product.PriceInCents / 100m,
            currency,
            product.Interval,
            product.IntervalUnit);
    }

    private static ShopperSubscription MapSubscription(MaxioSubscription subscription)
    {
        return new ShopperSubscription(
            subscription.Id,
            subscription.Product.Handle ?? string.Empty,
            subscription.Product.Name,
            subscription.ProductPriceInCents,
            subscription.ProductPriceInCents / 100m,
            subscription.Currency,
            subscription.State,
            subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
            subscription.CurrentPeriodEndsAt);
    }

    private static string BuildCustomerReference(string userId) => $"eshop-user-{userId}";

    private static string BuildSubscriptionReference(string userId, string productHandle) =>
        $"eshop-subscription-{userId}-{productHandle}";

    private static (string FirstName, string LastName) DeriveNames(string email)
    {
        var localPart = email.Split('@', 2)[0];
        var parts = localPart.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => ("eShop", "Customer"),
            1 => (parts[0], "Customer"),
            _ => (parts[0], string.Join(' ', parts.Skip(1)))
        };
    }
}
