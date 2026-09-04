using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> EnrollmentLocks = new();
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "pending", "trialing", "assessing", "active", "soft_failure", "past_due", "suspended",
        "paused", "unpaid", "on_hold", "awaiting_signup"
    };

    private readonly IMaxioBillingClient _billingClient;
    private readonly MaxioOptions _options;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly CatalogContext _catalogContext;

    public MaxioSubscriptionService(
        IMaxioBillingClient billingClient,
        Microsoft.Extensions.Options.IOptions<MaxioOptions> options,
        UserManager<ApplicationUser> userManager,
        CatalogContext catalogContext)
    {
        _billingClient = billingClient;
        _options = options.Value;
        _userManager = userManager;
        _catalogContext = catalogContext;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
    {
        var products = await _billingClient.ListProductsAsync(_options.ProductFamilyHandle, cancellationToken);
        return products
            .Where(product => !string.IsNullOrWhiteSpace(product.Handle) && product.ArchivedAt is null)
            .Select(product => new SubscriptionPlanDto
            {
                Id = product.Id,
                Handle = product.Handle!,
                Name = product.Name,
                Description = product.Description,
                PriceInCents = product.PriceInCents,
                Interval = product.Interval,
                IntervalUnit = product.IntervalUnit,
                RequiresPaymentMethod = product.RequireCreditCard
            })
            .ToArray();
    }

    public async Task<SubscriptionDto> SubscribeAsync(string userName, string? requestedPlanHandle, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByNameAsync(userName)
            ?? throw new InvalidOperationException("The authenticated eShopOnWeb user could not be found.");

        var plans = await GetPlansAsync(cancellationToken);
        var plan = string.IsNullOrWhiteSpace(requestedPlanHandle)
            ? plans.FirstOrDefault()
            : plans.FirstOrDefault(candidate => string.Equals(candidate.Handle, requestedPlanHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
            throw new SubscriptionPlanNotFoundException(requestedPlanHandle ?? "(default)");

        var lockKey = $"{user.Id}:{plan.Handle}";
        var enrollmentLock = EnrollmentLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await enrollmentLock.WaitAsync(cancellationToken);
        try
        {
            return await SubscribeCoreAsync(user, plan, cancellationToken);
        }
        finally
        {
            enrollmentLock.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(string userName, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByNameAsync(userName)
            ?? throw new InvalidOperationException("The authenticated eShopOnWeb user could not be found.");
        var customer = await _billingClient.GetCustomerByReferenceAsync(user.Id, cancellationToken);
        if (customer is null)
            return Array.Empty<SubscriptionDto>();

        var subscriptions = await _billingClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(subscription => ToDto(subscription)).ToArray();
    }

    private async Task<SubscriptionDto> SubscribeCoreAsync(ApplicationUser user, SubscriptionPlanDto plan, CancellationToken cancellationToken)
    {
        var customer = await GetOrCreateCustomerAsync(user, cancellationToken);
        var subscriptions = await _billingClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var existing = subscriptions.FirstOrDefault(subscription =>
            IsLive(subscription) && string.Equals(subscription.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase));

        var enrollment = await GetOrCreateEnrollmentAsync(user.Id, plan.Handle, customer.Id, cancellationToken);
        if (existing is not null)
        {
            enrollment.MaxioCustomerId = customer.Id;
            enrollment.MaxioSubscriptionId = existing.Id;
            enrollment.UpdatedAtUtc = DateTime.UtcNow;
            await _catalogContext.SaveChangesAsync(cancellationToken);
            return ToDto(existing, plan);
        }

        // A terminal subscription may be followed by a new enrollment. Keep the old
        // record for correlation, but issue a fresh token for the new Maxio operation.
        if (enrollment.MaxioSubscriptionId.HasValue)
        {
            enrollment.MaxioSubscriptionId = null;
            enrollment.UniquenessToken = Guid.NewGuid().ToString("D");
            enrollment.UpdatedAtUtc = DateTime.UtcNow;
            await _catalogContext.SaveChangesAsync(cancellationToken);
        }

        MaxioSubscription created;
        try
        {
            created = await _billingClient.CreateSubscriptionAsync(
                plan.Handle,
                user.Id,
                $"eshop:{user.Id}:{plan.Handle}",
                enrollment.UniquenessToken,
                cancellationToken);
        }
        catch (MaxioApiException exception) when (exception.StatusCode == HttpStatusCode.Conflict)
        {
            // The uniqueness token means the original POST may have completed. Re-read
            // Maxio and reconcile the resulting subscription instead of creating another.
            subscriptions = await _billingClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            existing = subscriptions.FirstOrDefault(subscription =>
                IsLive(subscription) && string.Equals(subscription.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
                throw;
            created = existing;
        }

        enrollment.MaxioCustomerId = customer.Id;
        enrollment.MaxioSubscriptionId = created.Id;
        enrollment.UpdatedAtUtc = DateTime.UtcNow;
        await _catalogContext.SaveChangesAsync(cancellationToken);
        return ToDto(created, plan);
    }

    private async Task<MaxioCustomer> GetOrCreateCustomerAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var customer = await _billingClient.GetCustomerByReferenceAsync(user.Id, cancellationToken);
        if (customer is not null)
            return customer;

        var displayName = (user.UserName ?? "eShopOnWeb").Split('@')[0];
        try
        {
            return await _billingClient.CreateCustomerAsync(
                displayName,
                "Customer",
                user.Email ?? user.UserName ?? $"{user.Id}@invalid.local",
                user.Id,
                cancellationToken);
        }
        catch (MaxioApiException exception) when (exception.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Customer references are unique in Maxio. A concurrent request may have
            // won the create race, so look it up once more before surfacing the error.
            customer = await _billingClient.GetCustomerByReferenceAsync(user.Id, cancellationToken);
            if (customer is not null)
                return customer;
            throw;
        }
    }

    private async Task<SubscriptionEnrollment> GetOrCreateEnrollmentAsync(
        string userId,
        string productHandle,
        int customerId,
        CancellationToken cancellationToken)
    {
        var enrollment = await _catalogContext.SubscriptionEnrollments
            .SingleOrDefaultAsync(item => item.UserId == userId && item.ProductHandle == productHandle, cancellationToken);
        if (enrollment is not null)
            return enrollment;

        enrollment = new SubscriptionEnrollment
        {
            UserId = userId,
            ProductHandle = productHandle,
            MaxioCustomerId = customerId,
            UniquenessToken = Guid.NewGuid().ToString("D"),
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        _catalogContext.SubscriptionEnrollments.Add(enrollment);
        try
        {
            await _catalogContext.SaveChangesAsync(cancellationToken);
            return enrollment;
        }
        catch (DbUpdateException)
        {
            // A second application instance can win the unique user/plan insert. Use
            // the winner's token so both requests are protected by Maxio duplicate prevention.
            _catalogContext.Entry(enrollment).State = EntityState.Detached;
            return await _catalogContext.SubscriptionEnrollments
                .SingleAsync(item => item.UserId == userId && item.ProductHandle == productHandle, cancellationToken);
        }
    }

    private static bool IsLive(MaxioSubscription subscription) => LiveStates.Contains(subscription.State);

    private static SubscriptionDto ToDto(MaxioSubscription subscription, SubscriptionPlanDto? knownPlan = null)
    {
        return new SubscriptionDto
        {
            Id = subscription.Id,
            PlanHandle = subscription.Product?.Handle ?? knownPlan?.Handle ?? string.Empty,
            PlanName = subscription.Product?.Name ?? knownPlan?.Name ?? string.Empty,
            PriceInCents = subscription.PriceInCents != 0 ? subscription.PriceInCents : knownPlan?.PriceInCents ?? 0,
            State = subscription.State,
            NextBillingDate = subscription.CurrentPeriodEndsAt,
            NextAssessmentDate = subscription.NextAssessmentAt
        };
    }
}

public sealed class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string planHandle)
        : base($"The Maxio subscription plan '{planHandle}' is not available in the configured product family.")
    {
    }
}
