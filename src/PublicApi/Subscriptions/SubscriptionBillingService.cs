using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly TimeSpan ReservationLease = TimeSpan.FromMinutes(2);
    private readonly IMaxioBillingGateway _gateway;
    private readonly AppIdentityDbContext _identityContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SubscriptionOperationLock _operationLock;

    public SubscriptionBillingService(
        IMaxioBillingGateway gateway,
        AppIdentityDbContext identityContext,
        UserManager<ApplicationUser> userManager,
        SubscriptionOperationLock operationLock)
    {
        _gateway = gateway;
        _identityContext = identityContext;
        _userManager = userManager;
        _operationLock = operationLock;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var plans = await _gateway.ListPlansAsync(cancellationToken);
        return plans.Select(MapPlan).ToList();
    }

    public async Task<SubscriptionDto> SubscribeAsync(
        string applicationUserId,
        string productHandle,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new SubscriptionBillingException(
                StatusCodes.Status400BadRequest,
                "A product handle is required.");
        }

        productHandle = productHandle.Trim();
        var user = await GetUserAsync(applicationUserId);
        var plans = await _gateway.ListPlansAsync(cancellationToken);
        var selectedPlan = plans.SingleOrDefault(x =>
            string.Equals(x.Handle, productHandle, StringComparison.Ordinal));

        if (selectedPlan is null)
        {
            throw new SubscriptionBillingException(
                StatusCodes.Status404NotFound,
                "The requested subscription plan was not found.");
        }

        if (selectedPlan.RequiresPaymentMethod)
        {
            throw new SubscriptionBillingException(
                StatusCodes.Status422UnprocessableEntity,
                "This plan requires a payment method and cannot be enrolled through this flow.");
        }

        var lockKey = $"{applicationUserId}\n{selectedPlan.Handle}";
        using var operationLock = await _operationLock.AcquireAsync(lockKey, cancellationToken);

        var subscriptionReference = CreateSubscriptionReference(applicationUserId, selectedPlan.Handle);
        var leaseOwner = Guid.NewGuid().ToString("N");
        var reservation = await ReserveAsync(
            applicationUserId,
            selectedPlan.Handle,
            subscriptionReference,
            leaseOwner,
            cancellationToken);

        if (!reservation.OwnedByCaller)
        {
            var reconciled = await _gateway.FindSubscriptionAsync(subscriptionReference, cancellationToken);
            if (reconciled is not null)
            {
                await MarkActiveAsync(reservation.Enrollment, reconciled, cancellationToken);
                return MapSubscription(reconciled);
            }

            throw new SubscriptionBillingException(
                StatusCodes.Status409Conflict,
                "Enrollment for this plan is already in progress. Retry shortly to read its result.");
        }

        try
        {
            var subscription = await _gateway.FindSubscriptionAsync(subscriptionReference, cancellationToken);
            if (subscription is null)
            {
                var customer = await _gateway.EnsureCustomerAsync(
                    user.Id,
                    CustomerFirstName(user),
                    CustomerLastName(user),
                    user.Email!,
                    cancellationToken);

                try
                {
                    subscription = await _gateway.CreateSubscriptionAsync(
                        selectedPlan.Handle,
                        customer.Reference,
                        subscriptionReference,
                        cancellationToken);
                }
                catch (SubscriptionBillingException ex) when (ex.OutcomeUnknown)
                {
                    subscription = await _gateway.FindSubscriptionAsync(subscriptionReference, cancellationToken);
                    if (subscription is null)
                    {
                        throw;
                    }
                }

                reservation.Enrollment.MaxioCustomerId = customer.Id;
            }

            await MarkActiveAsync(reservation.Enrollment, subscription, cancellationToken);
            return MapSubscription(subscription);
        }
        catch
        {
            await MarkFailedAsync(reservation.Enrollment, leaseOwner, cancellationToken);
            throw;
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListForUserAsync(
        string applicationUserId,
        CancellationToken cancellationToken)
    {
        var user = await GetUserAsync(applicationUserId);
        var customer = await _gateway.FindCustomerAsync(user.Id, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var subscriptions = await _gateway.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(MapSubscription).ToList();
    }

    private async Task<ApplicationUser> GetUserAsync(string applicationUserId)
    {
        if (string.IsNullOrWhiteSpace(applicationUserId))
        {
            throw new SubscriptionBillingException(StatusCodes.Status401Unauthorized, "A valid user identity is required.");
        }

        var user = await _userManager.FindByIdAsync(applicationUserId);
        if (user is null || string.IsNullOrWhiteSpace(user.Email))
        {
            throw new SubscriptionBillingException(StatusCodes.Status401Unauthorized, "The authenticated user is not available.");
        }

        return user;
    }

    private async Task<(MaxioEnrollment Enrollment, bool OwnedByCaller)> ReserveAsync(
        string applicationUserId,
        string productHandle,
        string subscriptionReference,
        string leaseOwner,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var enrollment = await _identityContext.MaxioEnrollments.SingleOrDefaultAsync(
            x => x.ApplicationUserId == applicationUserId && x.ProductHandle == productHandle,
            cancellationToken);

        if (enrollment is null)
        {
            enrollment = new MaxioEnrollment
            {
                ApplicationUserId = applicationUserId,
                ProductHandle = productHandle,
                SubscriptionReference = subscriptionReference,
                Status = MaxioEnrollmentStatus.Pending,
                LeaseOwner = leaseOwner,
                LeaseExpiresAt = now.Add(ReservationLease),
                CreatedAt = now,
                UpdatedAt = now
            };
            _identityContext.MaxioEnrollments.Add(enrollment);

            try
            {
                await _identityContext.SaveChangesAsync(cancellationToken);
                return (enrollment, true);
            }
            catch (DbUpdateException)
            {
                _identityContext.Entry(enrollment).State = EntityState.Detached;
                enrollment = await _identityContext.MaxioEnrollments.SingleAsync(
                    x => x.ApplicationUserId == applicationUserId && x.ProductHandle == productHandle,
                    cancellationToken);
            }
        }

        if (enrollment.Status == MaxioEnrollmentStatus.Active ||
            (enrollment.Status == MaxioEnrollmentStatus.Pending && enrollment.LeaseExpiresAt > now))
        {
            return (enrollment, false);
        }

        if (_identityContext.Database.IsRelational())
        {
            var claimed = await _identityContext.MaxioEnrollments
                .Where(x => x.Id == enrollment.Id &&
                    (x.Status == MaxioEnrollmentStatus.Failed || x.LeaseExpiresAt <= now))
                .ExecuteUpdateAsync(
                    updates => updates
                        .SetProperty(x => x.Status, MaxioEnrollmentStatus.Pending)
                        .SetProperty(x => x.LeaseOwner, leaseOwner)
                        .SetProperty(x => x.LeaseExpiresAt, now.Add(ReservationLease))
                        .SetProperty(x => x.UpdatedAt, now),
                    cancellationToken);

            await _identityContext.Entry(enrollment).ReloadAsync(cancellationToken);
            return (enrollment, claimed == 1 && enrollment.LeaseOwner == leaseOwner);
        }

        enrollment.Status = MaxioEnrollmentStatus.Pending;
        enrollment.LeaseOwner = leaseOwner;
        enrollment.LeaseExpiresAt = now.Add(ReservationLease);
        enrollment.UpdatedAt = now;
        await _identityContext.SaveChangesAsync(cancellationToken);
        return (enrollment, true);
    }

    private async Task MarkActiveAsync(
        MaxioEnrollment enrollment,
        BillingSubscription subscription,
        CancellationToken cancellationToken)
    {
        enrollment.MaxioSubscriptionId = subscription.Id;
        enrollment.Status = MaxioEnrollmentStatus.Active;
        enrollment.LeaseOwner = null;
        enrollment.LeaseExpiresAt = null;
        enrollment.UpdatedAt = DateTimeOffset.UtcNow;
        await _identityContext.SaveChangesAsync(cancellationToken);
    }

    private async Task MarkFailedAsync(
        MaxioEnrollment enrollment,
        string leaseOwner,
        CancellationToken cancellationToken)
    {
        if (enrollment.LeaseOwner != leaseOwner)
        {
            return;
        }

        enrollment.Status = MaxioEnrollmentStatus.Failed;
        enrollment.LeaseOwner = null;
        enrollment.LeaseExpiresAt = null;
        enrollment.UpdatedAt = DateTimeOffset.UtcNow;

        try
        {
            await _identityContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception)
        {
            // Preserve the billing failure; the lease also expires independently.
        }
    }

    private static string CreateSubscriptionReference(string applicationUserId, string productHandle)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{applicationUserId}\n{productHandle}"));
        return $"eshop-{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private static string CustomerFirstName(ApplicationUser user)
    {
        if (!string.IsNullOrWhiteSpace(user.FirstName))
        {
            return user.FirstName.Trim();
        }

        var emailLocalPart = user.Email!.Split('@', 2)[0].Trim();
        return string.IsNullOrWhiteSpace(emailLocalPart) ? "eShop" : emailLocalPart;
    }

    private static string CustomerLastName(ApplicationUser user) =>
        string.IsNullOrWhiteSpace(user.LastName) ? "Customer" : user.LastName.Trim();

    private static SubscriptionPlanDto MapPlan(BillingPlan plan) =>
        new(
            plan.Id,
            plan.Handle,
            plan.Name,
            plan.Description,
            plan.PriceInCents,
            plan.PriceInCents / 100m,
            plan.Interval,
            plan.IntervalUnit,
            !plan.RequiresPaymentMethod);

    private static SubscriptionDto MapSubscription(BillingSubscription subscription) =>
        new(
            subscription.Id,
            subscription.ProductHandle,
            subscription.ProductName,
            subscription.PriceInCents,
            subscription.PriceInCents / 100m,
            subscription.State,
            subscription.NextBillingDate,
            subscription.CurrentPeriodEndsAt,
            subscription.Currency,
            subscription.Reference);
}
