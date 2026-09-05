using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionService
{
    private readonly MaxioClient _maxio;
    private readonly AppIdentityDbContext _identityContext;
    private readonly UserManager<ApplicationUser> _userManager;

    public SubscriptionService(MaxioClient maxio, AppIdentityDbContext identityContext, UserManager<ApplicationUser> userManager)
    {
        _maxio = maxio;
        _identityContext = identityContext;
        _userManager = userManager;
    }

    public async Task<IReadOnlyList<SubscriptionPlanResponse>> GetPlansAsync(CancellationToken cancellationToken)
        => (await _maxio.GetPlansAsync(cancellationToken))
            .Where(plan => plan.ArchivedAt is null)
            .Select(plan => new SubscriptionPlanResponse(plan.Handle, plan.Name, plan.Description, plan.PriceInCents, plan.Interval, plan.IntervalUnit))
            .ToList();

    public async Task<SubscriptionResponse> SubscribeAsync(string userName, string requestedPlanHandle, CancellationToken cancellationToken)
    {
        var user = await GetUserAsync(userName);
        var planHandle = requestedPlanHandle.Trim();
        var plan = (await _maxio.GetPlansAsync(cancellationToken)).SingleOrDefault(item =>
            item.ArchivedAt is null && string.Equals(item.Handle, planHandle, StringComparison.Ordinal));
        if (plan is null) throw new SubscriptionValidationException("The requested subscription plan is not available.");

        var customer = await EnsureCustomerAsync(user, cancellationToken);
        var reference = SubscriptionReference(user.Id, plan.Handle);

        // This lookup makes an enrollment idempotent even when a local database was restored or lost.
        var existing = await _maxio.FindSubscriptionAsync(reference, cancellationToken);
        if (existing is not null)
        {
            await RecordEnrollmentAsync(user.Id, plan.Handle, reference, existing.Id, cancellationToken);
            return ToResponse(existing, plan);
        }

        var enrollment = await GetOrCreateEnrollmentAsync(user.Id, plan.Handle, reference, cancellationToken);
        if (enrollment.MaxioSubscriptionId is int knownId)
        {
            var known = await _maxio.FindSubscriptionAsync(reference, cancellationToken);
            if (known is not null) return ToResponse(known, plan);
            enrollment.MaxioSubscriptionId = null;
            await _identityContext.SaveChangesAsync(cancellationToken);
        }

        try
        {
            var created = await _maxio.CreateSubscriptionAsync(plan.Handle, CustomerReference(user.Id), reference, enrollment.UniquenessToken, cancellationToken);
            enrollment.MaxioSubscriptionId = created.Id;
            await _identityContext.SaveChangesAsync(cancellationToken);
            return ToResponse(created, plan);
        }
        catch (MaxioApiException exception) when (exception.StatusCode == HttpStatusCode.Conflict)
        {
            // Maxio returns 409 for a reused uniqueness_token. Recover the first request's result by reference.
            var recovered = await _maxio.FindSubscriptionAsync(reference, cancellationToken);
            if (recovered is not null)
            {
                enrollment.MaxioSubscriptionId = recovered.Id;
                await _identityContext.SaveChangesAsync(cancellationToken);
                return ToResponse(recovered, plan);
            }

            throw;
        }
    }

    public async Task<MySubscriptionsResponse> GetMySubscriptionsAsync(string userName, CancellationToken cancellationToken)
    {
        var user = await GetUserAsync(userName);
        // A read must not create a billable-system customer. Enrollment creates the customer on demand.
        var customer = await _maxio.FindCustomerAsync(CustomerReference(user.Id), cancellationToken);
        if (customer is null) return new MySubscriptionsResponse(Array.Empty<SubscriptionResponse>());
        var subscriptions = await _maxio.GetCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return new MySubscriptionsResponse(subscriptions
            .Where(subscription => subscription.Product is not null)
            .Select(subscription => ToResponse(subscription, subscription.Product!))
            .ToList());
    }

    private async Task<ApplicationUser> GetUserAsync(string userName)
        => await _userManager.FindByNameAsync(userName) ?? throw new SubscriptionValidationException("The authenticated user no longer exists.");

    private async Task<MaxioCustomer> EnsureCustomerAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var reference = CustomerReference(user.Id);
        var customer = await _maxio.FindCustomerAsync(reference, cancellationToken);
        if (customer is null)
        {
            var (firstName, lastName) = NamesFromUser(user);
            try
            {
                customer = await _maxio.CreateCustomerAsync(new MaxioCustomerInput(firstName, lastName, user.Email ?? user.UserName ?? "unknown@example.invalid", reference), Guid.NewGuid().ToString("N"), cancellationToken);
            }
            catch (MaxioApiException exception) when (exception.StatusCode == HttpStatusCode.UnprocessableEntity || exception.StatusCode == HttpStatusCode.Conflict)
            {
                customer = await _maxio.FindCustomerAsync(reference, cancellationToken);
                if (customer is null) throw;
            }
        }

        return customer;
    }

    private async Task<MaxioSubscriptionEnrollment> GetOrCreateEnrollmentAsync(string userId, string planHandle, string reference, CancellationToken cancellationToken)
    {
        var existing = await _identityContext.MaxioSubscriptionEnrollments.SingleOrDefaultAsync(item => item.UserId == userId && item.PlanHandle == planHandle, cancellationToken);
        if (existing is not null) return existing;

        var enrollment = new MaxioSubscriptionEnrollment
        {
            UserId = userId,
            PlanHandle = planHandle,
            SubscriptionReference = reference,
            // A deterministic UUID lets simultaneous requests from separate app instances share
            // Maxio's duplicate-prevention token before either instance can persist its record.
            UniquenessToken = SubscriptionUniquenessToken(userId, planHandle),
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        _identityContext.MaxioSubscriptionEnrollments.Add(enrollment);
        try
        {
            await _identityContext.SaveChangesAsync(cancellationToken);
            return enrollment;
        }
        catch (DbUpdateException)
        {
            return await _identityContext.MaxioSubscriptionEnrollments.SingleAsync(item => item.UserId == userId && item.PlanHandle == planHandle, cancellationToken);
        }
    }

    private async Task RecordEnrollmentAsync(string userId, string planHandle, string reference, int maxioSubscriptionId, CancellationToken cancellationToken)
    {
        var enrollment = await GetOrCreateEnrollmentAsync(userId, planHandle, reference, cancellationToken);
        if (enrollment.MaxioSubscriptionId != maxioSubscriptionId)
        {
            enrollment.MaxioSubscriptionId = maxioSubscriptionId;
            await _identityContext.SaveChangesAsync(cancellationToken);
        }
    }

    private static SubscriptionResponse ToResponse(MaxioSubscription subscription, MaxioProduct plan)
        => new(subscription.Id, plan.Handle, plan.Name, subscription.ProductPriceInCents ?? plan.PriceInCents, subscription.State,
            subscription.NextBillingAt ?? subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt);

    private static string CustomerReference(string userId) => $"eshop-customer-{userId}";

    private static string SubscriptionReference(string userId, string planHandle)
    {
        var input = Encoding.UTF8.GetBytes($"{userId}:{planHandle}");
        return $"eshop-sub-{Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant()[..40]}";
    }

    private static string SubscriptionUniquenessToken(string userId, string planHandle)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"subscription:{userId}:{planHandle}"));
        return new Guid(hash[..16]).ToString("N");
    }

    private static (string FirstName, string LastName) NamesFromUser(ApplicationUser user)
    {
        var localPart = (user.Email ?? user.UserName ?? "Shopper").Split('@')[0];
        var pieces = localPart.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);
        return (pieces.ElementAtOrDefault(0) ?? "Shopper", pieces.ElementAtOrDefault(1) ?? "Customer");
    }
}

public sealed class SubscriptionValidationException : Exception
{
    public SubscriptionValidationException(string message) : base(message) { }
}
