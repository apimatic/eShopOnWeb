using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private const string Pending = "pending";
    private const string InProgress = "in_progress";
    private const string Confirmed = "confirmed";
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ProviderBudget = TimeSpan.FromSeconds(25);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> EnrollmentLocks = new();

    private readonly AppIdentityDbContext _identityContext;
    private readonly MaxioBillingGateway _gateway;
    private readonly MaxioOptions _options;

    public MaxioSubscriptionBillingService(
        AppIdentityDbContext identityContext,
        MaxioBillingGateway gateway,
        IOptions<MaxioOptions> options)
    {
        _identityContext = identityContext;
        _gateway = gateway;
        _options = options.Value;
    }

    public Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken) =>
        BoundedAsync(ct => _gateway.ListPlansAsync(_options.ProductFamilyHandle, ct), cancellationToken);

    public async Task<SubscriptionEnrollmentResult> SubscribeAsync(ShopperProfile shopper, string planHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new MaxioBillingException("A subscription plan handle is required.", 400);
        }

        var approvedPlan = (await ListPlansAsync(cancellationToken)).SingleOrDefault(x => x.Handle == planHandle);
        if (approvedPlan is null)
        {
            throw new MaxioBillingException("The requested subscription plan is unavailable.", 400);
        }

        var enrollment = await ClaimEnrollmentAsync(shopper.UserId, approvedPlan.Handle, cancellationToken);
        if (enrollment.Status == Confirmed)
        {
            return new SubscriptionEnrollmentResult(ToStoredSubscription(enrollment), false, enrollment.SubscriptionReference);
        }

        if (enrollment.Status != InProgress)
        {
            return new SubscriptionEnrollmentResult(null, true, enrollment.SubscriptionReference);
        }

        try
        {
            var existingSubscription = await BoundedAsync(ct => _gateway.FindSubscriptionAsync(enrollment.SubscriptionReference, ct), cancellationToken);
            if (existingSubscription is not null)
            {
                await ConfirmAsync(enrollment, existingSubscription, cancellationToken);
                return new SubscriptionEnrollmentResult(existingSubscription, false, enrollment.SubscriptionReference);
            }

            var customer = await BoundedAsync(ct => _gateway.EnsureCustomerAsync(shopper, enrollment.CustomerReference, ct), cancellationToken);
            enrollment.MaxioCustomerId = customer.Id;
            enrollment.UpdatedAt = DateTimeOffset.UtcNow;
            await _identityContext.SaveChangesAsync(cancellationToken);

            var subscription = await BoundedAsync(
                ct => _gateway.CreateSubscriptionAsync(customer.Id, approvedPlan.Handle, enrollment.SubscriptionReference, ct), cancellationToken);
            await ConfirmAsync(enrollment, subscription, cancellationToken);
            return new SubscriptionEnrollmentResult(subscription, false, enrollment.SubscriptionReference);
        }
        catch (MaxioWriteRetryBlockedException)
        {
            // The first POST may have reached Maxio. Do not issue another write; a later request reconciles by reference.
            return new SubscriptionEnrollmentResult(null, true, enrollment.SubscriptionReference);
        }
        catch
        {
            enrollment.Status = Pending;
            enrollment.LeaseToken = null;
            enrollment.LeaseExpiresAt = null;
            enrollment.UpdatedAt = DateTimeOffset.UtcNow;
            await _identityContext.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<IReadOnlyList<BillingSubscription>> ListMySubscriptionsAsync(ShopperProfile shopper, CancellationToken cancellationToken)
    {
        var customerReference = CustomerReference(shopper.UserId);
        var customer = await BoundedAsync(ct => _gateway.FindCustomerAsync(customerReference, ct), cancellationToken);
        return customer is null
            ? Array.Empty<BillingSubscription>()
            : await BoundedAsync(ct => _gateway.ListCustomerSubscriptionsAsync(customer.Id, ct), cancellationToken);
    }

    private async Task<MaxioSubscriptionEnrollment> ClaimEnrollmentAsync(string userId, string planHandle, CancellationToken cancellationToken)
    {
        var key = $"{userId}:{planHandle}";
        var gate = EnrollmentLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var enrollment = await _identityContext.MaxioSubscriptionEnrollments
                .SingleOrDefaultAsync(x => x.UserId == userId && x.PlanHandle == planHandle, cancellationToken);

            if (enrollment is null)
            {
                enrollment = new MaxioSubscriptionEnrollment
                {
                    UserId = userId,
                    PlanHandle = planHandle,
                    CustomerReference = CustomerReference(userId),
                    SubscriptionReference = $"eshop-sub-{Guid.NewGuid():N}",
                    Status = InProgress,
                    LeaseToken = Guid.NewGuid().ToString("N"),
                    LeaseExpiresAt = now.Add(LeaseDuration),
                    CreatedAt = now,
                    UpdatedAt = now
                };
                _identityContext.MaxioSubscriptionEnrollments.Add(enrollment);

                try
                {
                    await _identityContext.SaveChangesAsync(cancellationToken);
                    return enrollment;
                }
                catch (DbUpdateException)
                {
                    _identityContext.Entry(enrollment).State = EntityState.Detached;
                    enrollment = await _identityContext.MaxioSubscriptionEnrollments
                        .SingleAsync(x => x.UserId == userId && x.PlanHandle == planHandle, cancellationToken);
                }
            }

            if (enrollment.Status == Confirmed || (enrollment.Status == InProgress && enrollment.LeaseExpiresAt > now))
            {
                return enrollment;
            }

            enrollment.Status = InProgress;
            enrollment.LeaseToken = Guid.NewGuid().ToString("N");
            enrollment.LeaseExpiresAt = now.Add(LeaseDuration);
            enrollment.UpdatedAt = now;
            await _identityContext.SaveChangesAsync(cancellationToken);
            return enrollment;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task ConfirmAsync(MaxioSubscriptionEnrollment enrollment, BillingSubscription subscription, CancellationToken cancellationToken)
    {
        enrollment.Status = Confirmed;
        enrollment.MaxioSubscriptionId = subscription.Id;
        enrollment.PlanName = subscription.PlanName;
        enrollment.PriceInCents = subscription.PriceInCents;
        enrollment.Currency = subscription.Currency;
        enrollment.SubscriptionState = subscription.State;
        enrollment.NextBillingAt = subscription.NextBillingAt;
        enrollment.LeaseToken = null;
        enrollment.LeaseExpiresAt = null;
        enrollment.UpdatedAt = DateTimeOffset.UtcNow;
        await _identityContext.SaveChangesAsync(cancellationToken);
    }

    private static BillingSubscription ToStoredSubscription(MaxioSubscriptionEnrollment enrollment)
    {
        if (enrollment.MaxioSubscriptionId is not int subscriptionId || string.IsNullOrWhiteSpace(enrollment.PlanName) ||
            enrollment.PriceInCents is not int price || string.IsNullOrWhiteSpace(enrollment.SubscriptionState))
        {
            throw new MaxioBillingException("The stored subscription enrollment is incomplete.", 502);
        }

        return new BillingSubscription(subscriptionId, enrollment.SubscriptionReference, enrollment.PlanHandle, enrollment.PlanName,
            price, enrollment.Currency, enrollment.SubscriptionState, enrollment.NextBillingAt);
    }

    private static string CustomerReference(string userId) => $"eshop-customer-{userId}";

    private static async Task<T> BoundedAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(ProviderBudget);
        return await operation(linked.Token);
    }
}
