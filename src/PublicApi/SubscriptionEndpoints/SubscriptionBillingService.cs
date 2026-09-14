using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly TimeSpan EnrollmentLease = TimeSpan.FromMinutes(5);
    private readonly IMaxioBillingClient _maxio;
    private readonly ICurrentUserService _currentUserService;
    private readonly ISubscriptionEnrollmentLock _enrollmentLock;
    private readonly CatalogContext _catalogContext;
    private readonly MaxioOptions _options;
    private readonly TimeProvider _timeProvider;

    public SubscriptionBillingService(
        IMaxioBillingClient maxio,
        ICurrentUserService currentUserService,
        ISubscriptionEnrollmentLock enrollmentLock,
        CatalogContext catalogContext,
        IOptions<MaxioOptions> options,
        TimeProvider timeProvider)
    {
        _maxio = maxio;
        _currentUserService = currentUserService;
        _enrollmentLock = enrollmentLock;
        _catalogContext = catalogContext;
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    public Task<IReadOnlyList<BillingPlan>> GetPlansAsync(CancellationToken cancellationToken)
    {
        return _maxio.GetPlansAsync(cancellationToken);
    }

    public async Task<SubscribeResult> SubscribeAsync(
        ClaimsPrincipal principal,
        string productHandle,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new ArgumentException("A productHandle is required.", nameof(productHandle));
        }

        var plans = await _maxio.GetPlansAsync(cancellationToken);
        var plan = plans.SingleOrDefault(x => string.Equals(x.Handle, productHandle, StringComparison.Ordinal));
        if (plan == null)
        {
            throw new SubscriptionPlanNotFoundException(productHandle);
        }

        var user = await _currentUserService.GetAsync(principal, cancellationToken);
        var customerReference = CustomerReference(user.Id);
        var subscriptionReference = SubscriptionReference(user.Id, plan.Handle);

        using var enrollmentLock = await _enrollmentLock.AcquireAsync(subscriptionReference, cancellationToken);
        var reservation = await ReserveAsync(
            user.Id,
            plan.Handle,
            customerReference,
            subscriptionReference,
            cancellationToken);

        var existingSubscription = await _maxio.FindSubscriptionAsync(subscriptionReference, cancellationToken);
        if (existingSubscription != null)
        {
            ValidateOwnership(existingSubscription, customerReference, plan.Handle);
            await CompleteReservationAsync(reservation.Enrollment, existingSubscription, cancellationToken);
            return new SubscribeResult(Map(existingSubscription), false);
        }

        if (!reservation.OwnsLease)
        {
            if (string.Equals(
                    reservation.Enrollment.Status,
                    SubscriptionEnrollmentStatus.Synchronized,
                    StringComparison.Ordinal))
            {
                throw new SubscriptionConsistencyException();
            }

            var now = _timeProvider.GetUtcNow();
            if (reservation.Enrollment.LeaseExpiresAt > now)
            {
                throw new SubscriptionEnrollmentInProgressException();
            }

            reservation.Enrollment.RenewLease(now, now.Add(EnrollmentLease));
            try
            {
                await _catalogContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new SubscriptionEnrollmentInProgressException();
            }
        }

        var customer = await EnsureCustomerAsync(user, customerReference, cancellationToken);

        BillingSubscription subscription;
        var created = true;
        try
        {
            subscription = await _maxio.CreateSubscriptionAsync(
                customer.Id,
                plan.Handle,
                subscriptionReference,
                cancellationToken);
        }
        catch (MaxioApiException exception) when (exception.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var reconciled = await _maxio.FindSubscriptionAsync(subscriptionReference, cancellationToken);
            if (reconciled == null)
            {
                throw;
            }

            subscription = reconciled;
            created = false;
        }

        ValidateOwnership(subscription, customerReference, plan.Handle);
        await CompleteReservationAsync(reservation.Enrollment, subscription, cancellationToken);
        return new SubscribeResult(Map(subscription), created);
    }

    public async Task<IReadOnlyList<SubscriptionDetails>> GetMySubscriptionsAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var user = await _currentUserService.GetAsync(principal, cancellationToken);
        var customer = await _maxio.FindCustomerAsync(CustomerReference(user.Id), cancellationToken);
        if (customer == null)
        {
            return Array.Empty<SubscriptionDetails>();
        }

        var subscriptions = await _maxio.GetCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions
            .Where(x => string.Equals(
                x.ProductFamilyHandle,
                _options.ProductFamilyHandle,
                StringComparison.Ordinal))
            .OrderBy(x => x.Id)
            .Select(Map)
            .ToList();
    }

    private async Task<Reservation> ReserveAsync(
        string userId,
        string productHandle,
        string customerReference,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        var existing = await _catalogContext.SubscriptionEnrollments.SingleOrDefaultAsync(
            x => x.UserId == userId && x.ProductHandle == productHandle,
            cancellationToken);
        if (existing != null)
        {
            return new Reservation(existing, false);
        }

        var now = _timeProvider.GetUtcNow();
        var enrollment = new SubscriptionEnrollment(
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
            return new Reservation(enrollment, true);
        }
        catch (DbUpdateException)
        {
            _catalogContext.ChangeTracker.Clear();
            existing = await _catalogContext.SubscriptionEnrollments.SingleOrDefaultAsync(
                x => x.UserId == userId && x.ProductHandle == productHandle,
                cancellationToken);
            if (existing == null)
            {
                throw;
            }

            return new Reservation(existing, false);
        }
    }

    private async Task<BillingCustomer> EnsureCustomerAsync(
        CurrentUser user,
        string customerReference,
        CancellationToken cancellationToken)
    {
        var customer = await _maxio.FindCustomerAsync(customerReference, cancellationToken);
        if (customer != null)
        {
            return customer;
        }

        var firstName = user.Email.Split('@', 2)[0];
        if (string.IsNullOrWhiteSpace(firstName))
        {
            firstName = "eShop";
        }

        try
        {
            return await _maxio.CreateCustomerAsync(
                customerReference,
                firstName,
                "eShopOnWeb",
                user.Email,
                cancellationToken);
        }
        catch (MaxioApiException exception) when (exception.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var reconciled = await _maxio.FindCustomerAsync(customerReference, cancellationToken);
            if (reconciled == null)
            {
                throw;
            }

            return reconciled;
        }
    }

    private async Task CompleteReservationAsync(
        SubscriptionEnrollment enrollment,
        BillingSubscription subscription,
        CancellationToken cancellationToken)
    {
        enrollment.Complete(subscription.CustomerId, subscription.Id, _timeProvider.GetUtcNow());
        try
        {
            await _catalogContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another instance reconciled the same deterministic Maxio reference first.
        }
    }

    private static void ValidateOwnership(
        BillingSubscription subscription,
        string customerReference,
        string productHandle)
    {
        if (!string.Equals(subscription.CustomerReference, customerReference, StringComparison.Ordinal) ||
            !string.Equals(subscription.ProductHandle, productHandle, StringComparison.Ordinal))
        {
            throw new SubscriptionConsistencyException();
        }
    }

    private static SubscriptionDetails Map(BillingSubscription subscription)
    {
        return new SubscriptionDetails(
            subscription.Id,
            subscription.ProductHandle,
            subscription.ProductName,
            subscription.PriceInCents,
            subscription.Interval,
            subscription.IntervalUnit,
            subscription.State,
            subscription.CurrentPeriodEndsAt,
            subscription.NextAssessmentAt);
    }

    private static string CustomerReference(string userId) => $"eshop-user:{userId}";

    private static string SubscriptionReference(string userId, string productHandle) =>
        $"eshop-subscription:{userId}:{productHandle}";

    private sealed record Reservation(SubscriptionEnrollment Enrollment, bool OwnsLease);
}
