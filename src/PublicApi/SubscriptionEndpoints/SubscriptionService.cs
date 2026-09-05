using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public interface ISubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlanResponse>> GetPlansAsync(CancellationToken cancellationToken);
    Task<SubscriptionResponse> SubscribeAsync(ClaimsPrincipal principal, SubscribeRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionResponse>> GetMySubscriptionsAsync(ClaimsPrincipal principal, CancellationToken cancellationToken);
}

public sealed class SubscriptionService : ISubscriptionService
{
    private readonly IMaxioBillingClient _maxio;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AppIdentityDbContext _identityDb;
    private readonly ILogger<SubscriptionService> _logger;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> EnrollmentLocks = new(StringComparer.Ordinal);

    public SubscriptionService(IMaxioBillingClient maxio, UserManager<ApplicationUser> userManager, AppIdentityDbContext identityDb, ILogger<SubscriptionService> logger)
    {
        _maxio = maxio;
        _userManager = userManager;
        _identityDb = identityDb;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanResponse>> GetPlansAsync(CancellationToken cancellationToken) =>
        (await _maxio.ListPlansAsync(cancellationToken)).Select(ToResponse).ToArray();

    public async Task<SubscriptionResponse> SubscribeAsync(ClaimsPrincipal principal, SubscribeRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle)) throw new ArgumentException("planHandle is required.");
        var user = await GetAuthenticatedUserAsync(principal);
        var planHandle = request.PlanHandle.Trim();
        var lockKey = user.Id + ":" + planHandle;
        var gate = EnrollmentLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));

        await gate.WaitAsync(cancellationToken);
        try
        {
            var plan = await _maxio.GetPlanAsync(planHandle, cancellationToken)
                ?? throw new ArgumentException("The requested plan is not available.");
            var enrollment = await GetOrReserveEnrollmentAsync(user.Id, plan.Handle, cancellationToken);

            // Lookup-by-reference makes retry safe if the HTTP response was lost after Maxio accepted it.
            var existingSubscription = await _maxio.FindSubscriptionAsync(enrollment.SubscriptionReference, cancellationToken);
            if (existingSubscription is not null)
            {
                await CompleteEnrollmentAsync(enrollment, existingSubscription, cancellationToken);
                return ToResponse(existingSubscription);
            }

            var customerReference = CustomerReference(user.Id);
            var customer = await _maxio.FindCustomerAsync(customerReference, cancellationToken);
            if (customer is null)
            {
                try
                {
                    customer = await _maxio.CreateCustomerAsync(customerReference, RequiredEmail(user), "eShop", "Shopper", cancellationToken);
                }
                catch (MaxioBillingException exception) when (exception.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
                {
                    // The unique customer reference may have been created by another request/process.
                    customer = await _maxio.FindCustomerAsync(customerReference, cancellationToken);
                    if (customer is null) throw;
                }
            }

            enrollment.MaxioCustomerId = customer.Id;
            await _identityDb.SaveChangesAsync(cancellationToken);

            var subscription = await _maxio.CreateSubscriptionAsync(customer.Id, plan.Handle, enrollment.SubscriptionReference, cancellationToken);
            await CompleteEnrollmentAsync(enrollment, subscription, cancellationToken);
            return ToResponse(subscription);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unable to enroll the authenticated shopper in Maxio plan {PlanHandle}.", planHandle);
            throw;
        }
        finally
        {
            gate.Release();
            EnrollmentLocks.TryRemove(lockKey, out _);
        }
    }

    public async Task<IReadOnlyList<SubscriptionResponse>> GetMySubscriptionsAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var user = await GetAuthenticatedUserAsync(principal);
        var customer = await _maxio.FindCustomerAsync(CustomerReference(user.Id), cancellationToken);
        if (customer is null) return Array.Empty<SubscriptionResponse>();

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(ToResponse).ToArray();
    }

    private async Task<ApplicationUser> GetAuthenticatedUserAsync(ClaimsPrincipal principal)
    {
        var username = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username)) throw new UnauthorizedAccessException("The JWT does not contain an authenticated user name.");
        return await _userManager.FindByNameAsync(username) ?? throw new UnauthorizedAccessException("The authenticated user no longer exists.");
    }

    private async Task<SubscriptionEnrollment> GetOrReserveEnrollmentAsync(string userId, string planHandle, CancellationToken cancellationToken)
    {
        var enrollment = await _identityDb.SubscriptionEnrollments.SingleOrDefaultAsync(item => item.UserId == userId && item.PlanHandle == planHandle, cancellationToken);
        if (enrollment is not null) return enrollment;

        enrollment = new SubscriptionEnrollment
        {
            UserId = userId,
            PlanHandle = planHandle,
            SubscriptionReference = SubscriptionReference(userId, planHandle),
            CreatedAt = DateTimeOffset.UtcNow
        };
        _identityDb.SubscriptionEnrollments.Add(enrollment);
        try
        {
            await _identityDb.SaveChangesAsync(cancellationToken);
            return enrollment;
        }
        catch (DbUpdateException)
        {
            // The database uniqueness constraint is the cross-instance idempotency reservation.
            _identityDb.ChangeTracker.Clear();
            return await _identityDb.SubscriptionEnrollments.SingleAsync(item => item.UserId == userId && item.PlanHandle == planHandle, cancellationToken);
        }
    }

    private async Task CompleteEnrollmentAsync(SubscriptionEnrollment enrollment, MaxioSubscription subscription, CancellationToken cancellationToken)
    {
        enrollment.MaxioSubscriptionId = subscription.Id;
        enrollment.CompletedAt ??= DateTimeOffset.UtcNow;
        await _identityDb.SaveChangesAsync(cancellationToken);
    }

    private static string CustomerReference(string userId) => "eshop-user-" + userId;

    private static string SubscriptionReference(string userId, string planHandle)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userId + "|" + planHandle))).ToLowerInvariant();
        return "eshop-sub-" + hash;
    }

    private static string RequiredEmail(ApplicationUser user) => !string.IsNullOrWhiteSpace(user.Email)
        ? user.Email
        : throw new InvalidOperationException("The authenticated user does not have an email address.");

    private static SubscriptionPlanResponse ToResponse(MaxioPlan plan) => new(plan.Handle, plan.Name, plan.Description, plan.PriceInCents, plan.Interval, plan.IntervalUnit);
    private static SubscriptionResponse ToResponse(MaxioSubscription subscription) => new(subscription.Id, subscription.PlanHandle, subscription.PlanName, subscription.PriceInCents, subscription.State, subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt);
}

public sealed class SubscribeRequest
{
    public string PlanHandle { get; init; } = string.Empty;
}

public sealed record SubscriptionPlanResponse(string Handle, string Name, string? Description, long PriceInCents, int Interval, string IntervalUnit);
public sealed record SubscriptionResponse(int SubscriptionId, string? PlanHandle, string? PlanName, long? PriceInCents, string State, DateTimeOffset? NextBillingAt);
