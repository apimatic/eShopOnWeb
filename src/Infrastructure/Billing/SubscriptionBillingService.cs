using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

internal sealed class SubscriptionBillingService : ISubscriptionBillingService
{
    private readonly AppIdentityDbContext _identityContext;
    private readonly IMaxioBillingGateway _gateway;
    private readonly AsyncKeyedLock _keyedLock;

    public SubscriptionBillingService(
        AppIdentityDbContext identityContext,
        IMaxioBillingGateway gateway,
        AsyncKeyedLock keyedLock)
    {
        _identityContext = identityContext;
        _gateway = gateway;
        _keyedLock = keyedLock;
    }

    public Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken) =>
        _gateway.GetPlansAsync(cancellationToken);

    public async Task<SubscriptionEnrollmentResult> SubscribeAsync(
        string userId,
        string productHandle,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new BillingProviderException(BillingFailureKind.InvalidRequest, "A product handle is required.", 422);
        }

        var user = await _identityContext.Users.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);
        if (user is null)
        {
            throw new BillingProviderException(BillingFailureKind.InvalidRequest, "The authenticated user no longer exists.", 401);
        }

        if (string.IsNullOrWhiteSpace(user.FirstName) ||
            string.IsNullOrWhiteSpace(user.LastName) ||
            string.IsNullOrWhiteSpace(user.Email))
        {
            throw new BillingProviderException(BillingFailureKind.InvalidRequest, "The account needs a first name, last name, and email before subscribing.", 422);
        }

        using var heldLock = await _keyedLock.AcquireAsync($"{userId}\n{productHandle}", cancellationToken);
        var plan = await _gateway.GetPlanAsync(productHandle, cancellationToken);
        var reference = BuildSubscriptionReference(userId, plan.Handle);
        var enrollment = await _identityContext.SubscriptionEnrollments
            .SingleOrDefaultAsync(candidate => candidate.UserId == userId && candidate.ProductHandle == plan.Handle, cancellationToken);

        if (enrollment is not null)
        {
            var reconciled = await _gateway.FindSubscriptionAsync(enrollment.MaxioSubscriptionReference, cancellationToken);
            if (reconciled is not null)
            {
                await MarkCreatedAsync(enrollment, reconciled.Id, cancellationToken);
                return new SubscriptionEnrollmentResult(reconciled, Created: false);
            }

            if (enrollment.State is SubscriptionEnrollmentState.Pending or SubscriptionEnrollmentState.Indeterminate)
            {
                throw new BillingOperationInProgressException("This subscription request is already in progress or awaiting reconciliation.");
            }

            enrollment.State = SubscriptionEnrollmentState.Pending;
            enrollment.UpdatedAt = DateTimeOffset.UtcNow;
            await _identityContext.SaveChangesAsync(cancellationToken);
        }
        else
        {
            enrollment = new SubscriptionEnrollment
            {
                UserId = userId,
                ProductHandle = plan.Handle,
                MaxioSubscriptionReference = reference,
                State = SubscriptionEnrollmentState.Pending,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            _identityContext.SubscriptionEnrollments.Add(enrollment);
            try
            {
                await _identityContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                _identityContext.ChangeTracker.Clear();
                enrollment = await _identityContext.SubscriptionEnrollments
                    .SingleAsync(candidate => candidate.UserId == userId && candidate.ProductHandle == plan.Handle, cancellationToken);

                var reconciled = await _gateway.FindSubscriptionAsync(enrollment.MaxioSubscriptionReference, cancellationToken);
                if (reconciled is not null)
                {
                    await MarkCreatedAsync(enrollment, reconciled.Id, cancellationToken);
                    return new SubscriptionEnrollmentResult(reconciled, Created: false);
                }

                throw new BillingOperationInProgressException("This subscription request is already in progress or awaiting reconciliation.");
            }
        }

        try
        {
            await _gateway.EnsureCustomerAsync(
                new BillingCustomerProfile(user.Id, user.FirstName, user.LastName, user.Email),
                cancellationToken);

            var subscription = await _gateway.CreateSubscriptionAsync(plan.Handle, user.Id, reference, cancellationToken);
            await MarkCreatedAsync(enrollment, subscription.Id, cancellationToken);
            return new SubscriptionEnrollmentResult(subscription, Created: true);
        }
        catch (BillingProviderException ex)
        {
            enrollment.State = ex.Kind == BillingFailureKind.Indeterminate
                ? SubscriptionEnrollmentState.Indeterminate
                : SubscriptionEnrollmentState.Failed;
            enrollment.UpdatedAt = DateTimeOffset.UtcNow;
            await _identityContext.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<IReadOnlyList<SubscriptionDetails>> GetSubscriptionsAsync(string userId, CancellationToken cancellationToken)
    {
        var userExists = await _identityContext.Users.AsNoTracking().AnyAsync(candidate => candidate.Id == userId, cancellationToken);
        if (!userExists)
        {
            throw new BillingProviderException(BillingFailureKind.InvalidRequest, "The authenticated user no longer exists.", 401);
        }

        return await _gateway.GetSubscriptionsAsync(userId, cancellationToken);
    }

    private async Task MarkCreatedAsync(SubscriptionEnrollment enrollment, int maxioSubscriptionId, CancellationToken cancellationToken)
    {
        enrollment.MaxioSubscriptionId = maxioSubscriptionId;
        enrollment.State = SubscriptionEnrollmentState.Created;
        enrollment.UpdatedAt = DateTimeOffset.UtcNow;
        await _identityContext.SaveChangesAsync(cancellationToken);
    }

    private static string BuildSubscriptionReference(string userId, string productHandle)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{userId}\n{productHandle}"));
        return $"eshop-{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }
}
