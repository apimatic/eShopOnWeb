using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionService : ISubscriptionService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> EnrollmentLocks = new();
    private static readonly TimeSpan EnrollmentLease = TimeSpan.FromMinutes(2);

    private readonly IMaxioClient _maxioClient;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly CatalogContext _catalogContext;

    public SubscriptionService(
        IMaxioClient maxioClient,
        UserManager<ApplicationUser> userManager,
        CatalogContext catalogContext)
    {
        _maxioClient = maxioClient;
        _userManager = userManager;
        _catalogContext = catalogContext;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var products = await _maxioClient.ListProductsAsync(cancellationToken);
        return products
            .OrderBy(product => product.PriceInCents)
            .Select(MapPlan)
            .ToList();
    }

    public async Task<SubscriptionDto> SubscribeAsync(
        string userName,
        string productHandle,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new SubscriptionPlanNotFoundException(productHandle);
        }

        var user = await FindUserAsync(userName);
        var products = await _maxioClient.ListProductsAsync(cancellationToken);
        var product = products.SingleOrDefault(candidate =>
            string.Equals(candidate.Handle, productHandle, StringComparison.Ordinal));
        if (product is null)
        {
            throw new SubscriptionPlanNotFoundException(productHandle);
        }

        var customerReference = SubscriptionReference.Customer(user.Id);
        var subscriptionReference = SubscriptionReference.Subscription(user.Id, productHandle);
        var enrollmentLock = EnrollmentLocks.GetOrAdd(subscriptionReference, _ => new SemaphoreSlim(1, 1));

        await enrollmentLock.WaitAsync(cancellationToken);
        try
        {
            var existingSubscription = await _maxioClient.FindSubscriptionAsync(subscriptionReference, cancellationToken);
            if (existingSubscription is not null)
            {
                ValidateOwnership(existingSubscription, customerReference, productHandle);
                await CompleteEnrollmentAsync(user.Id, productHandle, customerReference, subscriptionReference, existingSubscription);
                return MapSubscription(existingSubscription);
            }

            var enrollment = await ReserveEnrollmentAsync(
                user.Id,
                productHandle,
                customerReference,
                subscriptionReference,
                cancellationToken);

            try
            {
                var customer = await EnsureCustomerAsync(user, customerReference, cancellationToken);

                // A previous attempt may have completed in Maxio before its local write was committed.
                existingSubscription = await _maxioClient.FindSubscriptionAsync(subscriptionReference, cancellationToken);
                var subscription = existingSubscription ?? await _maxioClient.CreateSubscriptionAsync(
                    new CreateMaxioSubscription(productHandle, customerReference, subscriptionReference),
                    cancellationToken);

                ValidateOwnership(subscription, customerReference, productHandle);
                enrollment.Complete(customer.Id, subscription.Id, DateTimeOffset.UtcNow);
                await _catalogContext.SaveChangesAsync(cancellationToken);
                return MapSubscription(subscription);
            }
            catch (MaxioApiException exception) when ((int)exception.StatusCode is >= 400 and < 500)
            {
                enrollment.Fail(SafeError(exception), DateTimeOffset.UtcNow);
                await _catalogContext.SaveChangesAsync(CancellationToken.None);
                throw;
            }
            catch (MaxioTransportException)
            {
                // The POST outcome may be unknown. Keep the lease so an immediate retry cannot duplicate it;
                // the next attempt recovers by stable Maxio subscription reference before creating anything.
                throw;
            }
        }
        finally
        {
            enrollmentLock.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(
        string userName,
        CancellationToken cancellationToken)
    {
        var user = await FindUserAsync(userName);
        var customerReference = SubscriptionReference.Customer(user.Id);
        var customer = await _maxioClient.FindCustomerAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        if (!string.Equals(customer.Reference, customerReference, StringComparison.Ordinal))
        {
            throw new SubscriptionOwnershipException();
        }

        var subscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions
            .Where(subscription => subscription.Product is not null)
            .OrderBy(subscription => subscription.Id)
            .Select(MapSubscription)
            .ToList();
    }

    private async Task<ApplicationUser> FindUserAsync(string userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new ShopperNotFoundException();
        }

        return await _userManager.FindByNameAsync(userName) ?? throw new ShopperNotFoundException();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(
        ApplicationUser user,
        string customerReference,
        CancellationToken cancellationToken)
    {
        var customer = await _maxioClient.FindCustomerAsync(customerReference, cancellationToken);
        if (customer is not null)
        {
            return customer;
        }

        var email = user.Email ?? user.UserName ?? throw new ShopperNotFoundException();
        var firstName = email.Split('@', 2)[0];
        if (string.IsNullOrWhiteSpace(firstName))
        {
            firstName = "eShop";
        }

        try
        {
            return await _maxioClient.CreateCustomerAsync(
                new CreateMaxioCustomer(firstName, "Customer", email, customerReference),
                cancellationToken);
        }
        catch (MaxioApiException exception) when (exception.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Customer references are unique in Maxio. A concurrent creator may have won the race.
            var concurrentCustomer = await _maxioClient.FindCustomerAsync(customerReference, cancellationToken);
            if (concurrentCustomer is not null)
            {
                return concurrentCustomer;
            }

            throw;
        }
    }

    private async Task<SubscriptionEnrollment> ReserveEnrollmentAsync(
        string userId,
        string productHandle,
        string customerReference,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var enrollment = await _catalogContext.SubscriptionEnrollments.SingleOrDefaultAsync(
            item => item.UserId == userId && item.ProductHandle == productHandle,
            cancellationToken);

        if (enrollment is not null)
        {
            if (enrollment.Status == SubscriptionEnrollmentStatus.Pending && enrollment.LeaseExpiresAt > now)
            {
                throw new SubscriptionEnrollmentInProgressException();
            }

            enrollment.BeginAttempt(now, now.Add(EnrollmentLease));
            try
            {
                await _catalogContext.SaveChangesAsync(cancellationToken);
                return enrollment;
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new SubscriptionEnrollmentInProgressException();
            }
        }

        enrollment = new SubscriptionEnrollment(
            userId,
            productHandle,
            customerReference,
            subscriptionReference,
            now,
            now.Add(EnrollmentLease));
        _catalogContext.SubscriptionEnrollments.Add(enrollment);

        try
        {
            await _catalogContext.SaveChangesAsync(cancellationToken);
            return enrollment;
        }
        catch (DbUpdateException)
        {
            _catalogContext.Entry(enrollment).State = EntityState.Detached;
            throw new SubscriptionEnrollmentInProgressException();
        }
    }

    private async Task CompleteEnrollmentAsync(
        string userId,
        string productHandle,
        string customerReference,
        string subscriptionReference,
        MaxioSubscription subscription)
    {
        var enrollment = await _catalogContext.SubscriptionEnrollments.SingleOrDefaultAsync(item =>
            item.UserId == userId && item.ProductHandle == productHandle);
        var now = DateTimeOffset.UtcNow;
        if (enrollment is null)
        {
            enrollment = new SubscriptionEnrollment(
                userId,
                productHandle,
                customerReference,
                subscriptionReference,
                now,
                now);
            _catalogContext.SubscriptionEnrollments.Add(enrollment);
        }

        enrollment.Complete(subscription.Customer.Id, subscription.Id, now);
        try
        {
            await _catalogContext.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Another instance may have recovered the same Maxio record concurrently.
            // Maxio is authoritative and the remote subscription has already been validated.
        }
    }

    private static void ValidateOwnership(
        MaxioSubscription subscription,
        string customerReference,
        string productHandle)
    {
        if (!string.Equals(subscription.Customer.Reference, customerReference, StringComparison.Ordinal) ||
            !string.Equals(subscription.Product?.Handle, productHandle, StringComparison.Ordinal))
        {
            throw new SubscriptionOwnershipException();
        }
    }

    private static SubscriptionPlanDto MapPlan(MaxioProduct product) => new()
    {
        Handle = product.Handle!,
        Name = product.Name,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit,
        PricePointName = product.ProductPricePointName
    };

    private static SubscriptionDto MapSubscription(MaxioSubscription subscription)
    {
        var product = subscription.Product ?? throw new MaxioContractException(
            "Maxio returned a subscription without an associated product.");
        return new SubscriptionDto
        {
            Id = subscription.Id,
            Reference = subscription.Reference,
            State = subscription.State,
            ProductHandle = product.Handle ?? string.Empty,
            ProductName = product.Name,
            PriceInCents = subscription.ProductPriceInCents,
            Interval = product.Interval,
            IntervalUnit = product.IntervalUnit,
            PricePointName = subscription.ProductPricePointName ?? product.ProductPricePointName,
            NextBillingAt = subscription.NextAssessmentAt
        };
    }

    private static string SafeError(Exception exception)
    {
        var message = exception.Message;
        return message.Length <= 1000 ? message : message[..1000];
    }
}
