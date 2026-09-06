using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface IMaxioSubscriptionService
{
    Task<IReadOnlyList<MaxioPlan>> GetPlansAsync(CancellationToken cancellationToken);
    Task<EnrollmentResult> EnrollAsync(ClaimsPrincipal principal, string planHandle, CancellationToken cancellationToken);
    Task<IReadOnlyList<MaxioSubscription>> GetMySubscriptionsAsync(ClaimsPrincipal principal, CancellationToken cancellationToken);
}

public sealed class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> EnrollmentLocks = new(StringComparer.Ordinal);
    private static readonly TimeSpan PendingLease = TimeSpan.FromMinutes(2);

    private readonly IMaxioBillingClient _maxio;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AppIdentityDbContext _identityContext;

    public MaxioSubscriptionService(
        IMaxioBillingClient maxio,
        UserManager<ApplicationUser> userManager,
        AppIdentityDbContext identityContext)
    {
        _maxio = maxio;
        _userManager = userManager;
        _identityContext = identityContext;
    }

    public Task<IReadOnlyList<MaxioPlan>> GetPlansAsync(CancellationToken cancellationToken) =>
        _maxio.ListPlansAsync(cancellationToken);

    public async Task<EnrollmentResult> EnrollAsync(ClaimsPrincipal principal, string planHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new ArgumentException("planHandle is required.", nameof(planHandle));
        }

        var user = await GetUserAsync(principal);
        var plans = await _maxio.ListPlansAsync(cancellationToken);
        var plan = plans.SingleOrDefault(candidate => string.Equals(candidate.Handle, planHandle.Trim(), StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new ArgumentException("The requested plan is not available in the configured Maxio product family.", nameof(planHandle));
        }

        var customerReference = CustomerReference(user.Id);
        var subscriptionReference = SubscriptionReference(user.Id, plan.Handle);
        var enrollmentLock = EnrollmentLocks.GetOrAdd(subscriptionReference, _ => new SemaphoreSlim(1, 1));
        await enrollmentLock.WaitAsync(cancellationToken);
        try
        {
            var claim = await ClaimEnrollmentAsync(user.Id, plan.Handle, subscriptionReference, cancellationToken);
            if (!claim.IsOwner)
            {
                var existingSubscription = await _maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
                if (existingSubscription is not null)
                {
                    await CompleteAsync(claim.Enrollment, existingSubscription, cancellationToken);
                    return new EnrollmentResult(existingSubscription, false);
                }

                throw new SubscriptionInProgressException();
            }

            var knownSubscription = await _maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
            if (knownSubscription is not null)
            {
                await CompleteAsync(claim.Enrollment, knownSubscription, cancellationToken);
                return new EnrollmentResult(knownSubscription, false);
            }

            try
            {
                var customer = await EnsureCustomerAsync(customerReference, user, cancellationToken);
                claim.Enrollment.MaxioCustomerId = customer.Id;
                claim.Enrollment.UpdatedAt = DateTimeOffset.UtcNow;
                await _identityContext.SaveChangesAsync(cancellationToken);

                // The stable subscription reference is checked before create and on every retry.
                // It lets a lost response be reconciled without issuing a second enrollment.
                MaxioSubscription subscription;
                try
                {
                    subscription = await _maxio.CreateSubscriptionAsync(customerReference, subscriptionReference, plan.Handle, cancellationToken);
                }
                catch (MaxioApiException exception) when (exception.StatusCode == 422)
                {
                    // If another node completed the request after our preflight lookup, use the
                    // stable Maxio subscription reference instead of treating it as a new order.
                    var concurrentlyCreatedSubscription = await _maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
                    if (concurrentlyCreatedSubscription is null)
                    {
                        throw;
                    }

                    await CompleteAsync(claim.Enrollment, concurrentlyCreatedSubscription, cancellationToken);
                    return new EnrollmentResult(concurrentlyCreatedSubscription, false);
                }
                await CompleteAsync(claim.Enrollment, subscription, cancellationToken);
                return new EnrollmentResult(subscription, true);
            }
            catch (MaxioApiException exception) when (exception.StatusCode < 500)
            {
                claim.Enrollment.Status = SubscriptionEnrollment.StatusFailed;
                claim.Enrollment.UpdatedAt = DateTimeOffset.UtcNow;
                await _identityContext.SaveChangesAsync(cancellationToken);
                throw;
            }
        }
        finally
        {
            enrollmentLock.Release();
        }
    }

    public async Task<IReadOnlyList<MaxioSubscription>> GetMySubscriptionsAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var user = await GetUserAsync(principal);
        var customer = await _maxio.FindCustomerByReferenceAsync(CustomerReference(user.Id), cancellationToken);
        return customer is null
            ? Array.Empty<MaxioSubscription>()
            : await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    private async Task<(SubscriptionEnrollment Enrollment, bool IsOwner)> ClaimEnrollmentAsync(
        string userId,
        string planHandle,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        var enrollment = await _identityContext.SubscriptionEnrollments
            .SingleOrDefaultAsync(item => item.UserId == userId && item.PlanHandle == planHandle, cancellationToken);
        if (enrollment is not null)
        {
            if (enrollment.Status == SubscriptionEnrollment.StatusCompleted)
            {
                return (enrollment, false);
            }

            if (enrollment.Status == SubscriptionEnrollment.StatusPending && DateTimeOffset.UtcNow - enrollment.UpdatedAt < PendingLease)
            {
                return (enrollment, false);
            }

            enrollment.Status = SubscriptionEnrollment.StatusPending;
            enrollment.UpdatedAt = DateTimeOffset.UtcNow;
            await _identityContext.SaveChangesAsync(cancellationToken);
            return (enrollment, true);
        }

        enrollment = new SubscriptionEnrollment
        {
            UserId = userId,
            PlanHandle = planHandle,
            SubscriptionReference = subscriptionReference,
            Status = SubscriptionEnrollment.StatusPending,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _identityContext.SubscriptionEnrollments.Add(enrollment);
        try
        {
            await _identityContext.SaveChangesAsync(cancellationToken);
            return (enrollment, true);
        }
        catch (DbUpdateException)
        {
            // Another application instance won the database's unique enrollment claim.
            _identityContext.ChangeTracker.Clear();
            enrollment = await _identityContext.SubscriptionEnrollments
                .SingleAsync(item => item.UserId == userId && item.PlanHandle == planHandle, cancellationToken);
            return (enrollment, false);
        }
    }

    private async Task CompleteAsync(SubscriptionEnrollment enrollment, MaxioSubscription subscription, CancellationToken cancellationToken)
    {
        enrollment.MaxioCustomerId = subscription.CustomerId;
        enrollment.MaxioSubscriptionId = subscription.Id;
        enrollment.Status = SubscriptionEnrollment.StatusCompleted;
        enrollment.UpdatedAt = DateTimeOffset.UtcNow;
        if (_identityContext.Entry(enrollment).State == EntityState.Detached)
        {
            _identityContext.SubscriptionEnrollments.Update(enrollment);
        }
        await _identityContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(string customerReference, ApplicationUser user, CancellationToken cancellationToken)
    {
        var customer = await _maxio.FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (customer is not null)
        {
            return customer;
        }

        var (firstName, lastName) = ToCustomerName(user.Email ?? user.UserName ?? "shopper");
        try
        {
            return await _maxio.CreateCustomerAsync(customerReference, user.Email ?? user.UserName!, firstName, lastName, cancellationToken);
        }
        catch (MaxioApiException exception) when (exception.StatusCode == 422)
        {
            // Customer references are unique in Maxio. A concurrent request may have created it.
            var concurrentlyCreatedCustomer = await _maxio.FindCustomerByReferenceAsync(customerReference, cancellationToken);
            if (concurrentlyCreatedCustomer is not null)
            {
                return concurrentlyCreatedCustomer;
            }

            throw;
        }
    }

    private async Task<ApplicationUser> GetUserAsync(ClaimsPrincipal principal)
    {
        var username = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new UnauthorizedAccessException("The JWT does not include a user name.");
        }

        return await _userManager.FindByNameAsync(username)
            ?? throw new UnauthorizedAccessException("The authenticated user no longer exists.");
    }

    private static (string FirstName, string LastName) ToCustomerName(string source)
    {
        var localPart = source.Split('@', 2)[0].Replace('.', ' ').Replace('_', ' ').Trim();
        var words = localPart.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length switch
        {
            0 => ("eShopOnWeb", "Shopper"),
            1 => (words[0], "Shopper"),
            _ => (words[0], string.Join(' ', words.Skip(1)))
        };
    }

    private static string CustomerReference(string userId) => $"eshoponweb-user-{userId}";
    private static string SubscriptionReference(string userId, string planHandle) => $"eshoponweb-subscription-{userId}-{planHandle}";
}
