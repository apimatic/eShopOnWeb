using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioSubscriptionService : ISubscriptionService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserGates = new();
    private readonly AppIdentityDbContext _identityContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IMaxioSubscriptionGateway _gateway;
    private readonly MaxioSettings _settings;

    public MaxioSubscriptionService(
        AppIdentityDbContext identityContext,
        UserManager<ApplicationUser> userManager,
        IMaxioSubscriptionGateway gateway,
        IOptions<MaxioSettings> settings)
    {
        _identityContext = identityContext;
        _userManager = userManager;
        _gateway = gateway;
        _settings = settings.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var plans = await _gateway.ListPlansAsync(_settings.ProductFamilyHandle, cancellationToken);
        return plans.Select(MapPlan).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<SubscriptionDto> SubscribeAsync(ClaimsPrincipal principal, string planHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
            throw new SubscriptionApiException(400, "A plan handle is required.");

        var user = await GetUserAsync(principal);
        var userGate = UserGates.GetOrAdd(user.Id, _ => new SemaphoreSlim(1, 1));
        await userGate.WaitAsync(cancellationToken);
        try
        {
            var plans = await _gateway.ListPlansAsync(_settings.ProductFamilyHandle, cancellationToken);
            var plan = plans.SingleOrDefault(x => string.Equals(x.Handle, planHandle, StringComparison.Ordinal));
            if (plan is null)
                throw new SubscriptionApiException(422, "The requested subscription plan is not available.");

            var customerReference = CustomerReference(user);
            var (enrollment, leaseAcquired) = await AcquireEnrollmentLeaseAsync(user.Id, planHandle, customerReference, cancellationToken);
            if (!leaseAcquired)
                return MapEnrollment(enrollment);
            var customer = await ResolveCustomerAsync(user, cancellationToken);

            var subscription = await _gateway.FindSubscriptionAsync(enrollment.SubscriptionReference, cancellationToken)
                ?? await _gateway.CreateSubscriptionAsync(customer.CustomerId, plan.Handle!, enrollment.SubscriptionReference, cancellationToken);

            UpdateEnrollment(enrollment, subscription, planHandle);
            await _identityContext.SaveChangesAsync(cancellationToken);
            return MapSubscription(subscription, planHandle);
        }
        finally
        {
            userGate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var user = await GetUserAsync(principal);
        var customer = await _identityContext.MaxioCustomers.SingleOrDefaultAsync(x => x.UserId == user.Id, cancellationToken);
        if (customer is null)
            return [];

        var subscriptions = await _gateway.ListCustomerSubscriptionsAsync(customer.CustomerId, cancellationToken);
        return subscriptions
            .Where(x => string.Equals(x.Product?.ProductFamily?.Handle, _settings.ProductFamilyHandle, StringComparison.Ordinal))
            .Select(x => MapSubscription(x, x.Product?.Handle ?? string.Empty))
            .ToList();
    }

    private async Task<(MaxioSubscriptionEnrollment Enrollment, bool LeaseAcquired)> AcquireEnrollmentLeaseAsync(string userId, string planHandle, string customerReference, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var now = DateTimeOffset.UtcNow;
            var existing = await _identityContext.MaxioSubscriptionEnrollments
                .SingleOrDefaultAsync(x => x.UserId == userId && x.PlanHandle == planHandle, cancellationToken);
            if (existing is not null)
            {
                if (existing.SubscriptionId.HasValue)
                    return (existing, false);
                if (existing.ProcessingLeaseExpiresUtc > now)
                    throw new SubscriptionApiException(409, "A subscription enrollment is already in progress. Please retry shortly.");

                existing.ProcessingLeaseExpiresUtc = now.AddMinutes(2);
                try
                {
                    await _identityContext.SaveChangesAsync(cancellationToken);
                    return (existing, true);
                }
                catch (DbUpdateConcurrencyException)
                {
                    _identityContext.ChangeTracker.Clear();
                    continue;
                }
            }

            var enrollment = new MaxioSubscriptionEnrollment
            {
                UserId = userId,
                PlanHandle = planHandle,
                SubscriptionReference = $"eshop-subscription-{customerReference["eshop-user-".Length..]}-{planHandle}",
                ProcessingLeaseExpiresUtc = now.AddMinutes(2)
            };
            _identityContext.MaxioSubscriptionEnrollments.Add(enrollment);
            try
            {
                await _identityContext.SaveChangesAsync(cancellationToken);
                return (enrollment, true);
            }
            catch (DbUpdateException)
            {
                _identityContext.ChangeTracker.Clear();
            }
        }

        throw new SubscriptionApiException(409, "A subscription enrollment is already in progress. Please retry shortly.");
    }

    private async Task<MaxioCustomer> ResolveCustomerAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var mapping = await _identityContext.MaxioCustomers.SingleOrDefaultAsync(x => x.UserId == user.Id, cancellationToken);
        if (mapping is not null)
            return mapping;

        if (string.IsNullOrWhiteSpace(user.Email))
            throw new SubscriptionApiException(422, "Your account needs an email address before it can subscribe.");

        var (firstName, lastName) = CustomerName(user);
        var reference = CustomerReference(user);
        var customer = await _gateway.EnsureCustomerAsync(reference, firstName, lastName, user.Email, cancellationToken);
        if (customer.Id is null)
            throw new SubscriptionApiException(502, "Maxio returned an incomplete customer response.");

        mapping = new MaxioCustomer
        {
            UserId = user.Id,
            CustomerId = customer.Id.Value,
            CustomerReference = reference
        };
        _identityContext.MaxioCustomers.Add(mapping);
        try
        {
            await _identityContext.SaveChangesAsync(cancellationToken);
            return mapping;
        }
        catch (DbUpdateException)
        {
            _identityContext.ChangeTracker.Clear();
            var existing = await _identityContext.MaxioCustomers.SingleOrDefaultAsync(x => x.UserId == user.Id, cancellationToken);
            if (existing is not null)
                return existing;
            throw new SubscriptionApiException(409, "A customer enrollment is already in progress. Please retry shortly.");
        }
    }

    private async Task<ApplicationUser> GetUserAsync(ClaimsPrincipal principal)
    {
        var userName = principal.Identity?.Name;
        var user = string.IsNullOrWhiteSpace(userName) ? null : await _userManager.FindByNameAsync(userName);
        return user ?? throw new SubscriptionApiException(401, "The authenticated user could not be found.");
    }

    private static (string FirstName, string LastName) CustomerName(ApplicationUser user)
    {
        var localPart = (user.Email ?? user.UserName ?? "Customer").Split('@')[0].Trim();
        var words = localPart.Split(['.', '-', '_', ' '], StringSplitOptions.RemoveEmptyEntries);
        return words.Length switch
        {
            >= 2 => (words[0], words[^1]),
            1 => (words[0], "Customer"),
            _ => ("eShop", "Customer")
        };
    }

    private static string CustomerReference(ApplicationUser user)
    {
        var identifier = user.NormalizedUserName ?? user.UserName ?? user.Email
            ?? throw new SubscriptionApiException(422, "Your account needs a username before it can subscribe.");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identifier.Trim().ToUpperInvariant())));
        return $"eshop-user-{hash[..32]}";
    }

    private static void UpdateEnrollment(MaxioSubscriptionEnrollment enrollment, Subscription subscription, string planHandle)
    {
        enrollment.SubscriptionId = subscription.Id;
        enrollment.State = subscription.State?.Value;
        enrollment.ProductName = subscription.Product?.Name;
        enrollment.PriceInCents = subscription.ProductPriceInCents;
        enrollment.NextBillingAt = subscription.NextAssessmentAt;
        enrollment.ProcessingLeaseExpiresUtc = null;
        enrollment.UpdatedUtc = DateTimeOffset.UtcNow;
        enrollment.PlanHandle = planHandle;
    }

    private static SubscriptionPlanDto MapPlan(Product plan) => new()
    {
        Handle = plan.Handle ?? string.Empty,
        Name = plan.Name ?? plan.Handle ?? string.Empty,
        Description = plan.Description,
        Price = plan.PriceInCents / 100m,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit?.Value,
        RequiresCreditCard = plan.RequireCreditCard ?? plan.RequestCreditCard ?? false
    };

    private static SubscriptionDto MapSubscription(Subscription subscription, string planHandle) => new()
    {
        Id = subscription.Id,
        Reference = subscription.Reference,
        PlanHandle = subscription.Product?.Handle ?? planHandle,
        PlanName = subscription.Product?.Name,
        Price = subscription.ProductPriceInCents / 100m,
        State = subscription.State?.Value,
        NextBillingAt = subscription.NextAssessmentAt
    };

    private static SubscriptionDto MapEnrollment(MaxioSubscriptionEnrollment enrollment) => new()
    {
        Id = enrollment.SubscriptionId,
        Reference = enrollment.SubscriptionReference,
        PlanHandle = enrollment.PlanHandle,
        PlanName = enrollment.ProductName,
        Price = enrollment.PriceInCents / 100m,
        State = enrollment.State,
        NextBillingAt = enrollment.NextBillingAt
    };
}
