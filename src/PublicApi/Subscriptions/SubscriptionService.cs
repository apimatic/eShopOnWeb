using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public interface ISubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlanResponse>> ListPlansAsync(CancellationToken cancellationToken);
    Task<SubscriptionResponse> SubscribeAsync(string userName, string planHandle, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionResponse>> ListMySubscriptionsAsync(string userName, CancellationToken cancellationToken);
}

internal sealed class SubscriptionService : ISubscriptionService
{
    private readonly IMaxioAdvancedBillingClient _maxio;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AppIdentityDbContext _identityDb;
    private readonly SubscriptionEnrollmentLock _enrollmentLock;

    public SubscriptionService(IMaxioAdvancedBillingClient maxio, UserManager<ApplicationUser> userManager,
        AppIdentityDbContext identityDb, SubscriptionEnrollmentLock enrollmentLock)
    {
        _maxio = maxio;
        _userManager = userManager;
        _identityDb = identityDb;
        _enrollmentLock = enrollmentLock;
    }

    public async Task<IReadOnlyList<SubscriptionPlanResponse>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var products = await _maxio.ListProductsAsync(cancellationToken);
        return products.Where(x => x.ArchivedAt is null && !string.IsNullOrWhiteSpace(x.Handle))
            .Select(ToPlanResponse)
            .OrderBy(x => x.PriceInCents)
            .ToArray();
    }

    public async Task<SubscriptionResponse> SubscribeAsync(string userName, string planHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new SubscriptionValidationException("planHandle is required.");
        }

        var user = await GetUserAsync(userName);
        var normalizedHandle = planHandle.Trim();
        using var lease = await _enrollmentLock.AcquireAsync($"{user.Id}:{normalizedHandle}", cancellationToken);

        var plan = (await _maxio.ListProductsAsync(cancellationToken)).SingleOrDefault(x =>
            x.ArchivedAt is null && string.Equals(x.Handle, normalizedHandle, StringComparison.Ordinal));
        if (plan is null)
        {
            throw new SubscriptionValidationException("The selected subscription plan is not available.");
        }

        var customer = await EnsureCustomerAsync(user, cancellationToken);
        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var existing = subscriptions.FirstOrDefault(x =>
            string.Equals(x.Product?.Handle, normalizedHandle, StringComparison.Ordinal) &&
            !string.Equals(x.State, "canceled", StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            await SaveEnrollmentAsync(user.Id, normalizedHandle, existing.Id, cancellationToken);
            return ToSubscriptionResponse(existing, plan);
        }

        var subscription = await _maxio.CreateSubscriptionAsync(customer.Id, normalizedHandle,
            SubscriptionReference(user.Id, normalizedHandle), cancellationToken);
        await SaveEnrollmentAsync(user.Id, normalizedHandle, subscription.Id, cancellationToken);
        return ToSubscriptionResponse(subscription, plan);
    }

    public async Task<IReadOnlyList<SubscriptionResponse>> ListMySubscriptionsAsync(string userName, CancellationToken cancellationToken)
    {
        var user = await GetUserAsync(userName);
        var customer = await FindCustomerAsync(user, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionResponse>();
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(x => ToSubscriptionResponse(x, x.Product)).ToArray();
    }

    private async Task<ApplicationUser> GetUserAsync(string userName)
    {
        return await _userManager.FindByNameAsync(userName)
            ?? throw new SubscriptionValidationException("The authenticated user no longer exists.");
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var customer = await FindCustomerAsync(user, cancellationToken);
        if (customer is not null)
        {
            return customer;
        }

        var email = user.Email ?? user.UserName;
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new SubscriptionValidationException("An email address is required to subscribe.");
        }
        var name = email.Split('@', 2)[0];
        var firstName = string.IsNullOrWhiteSpace(name) ? "eShop" : name;

        try
        {
            customer = await _maxio.CreateCustomerAsync(firstName, "Shopper", email, CustomerReference(user.Id), cancellationToken);
        }
        catch (MaxioApiException exception) when (exception.StatusCode == HttpStatusCode.UnprocessableEntity || exception.StatusCode == HttpStatusCode.Conflict)
        {
            // Customer reference is unique in Maxio. A competing request can therefore safely be recovered by lookup.
            var existingCustomer = await _maxio.FindCustomerByReferenceAsync(CustomerReference(user.Id), cancellationToken);
            if (existingCustomer is null)
            {
                throw;
            }
            customer = existingCustomer;
        }

        await SaveCustomerLinkAsync(user.Id, customer.Id, cancellationToken);
        return customer;
    }

    private async Task<MaxioCustomer?> FindCustomerAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var linked = await _identityDb.MaxioCustomerLinks.FindAsync(new object[] { user.Id }, cancellationToken);
        if (linked is not null)
        {
            // The link is an optimization only. Confirm it against the unique Maxio reference before using it.
            var byReference = await _maxio.FindCustomerByReferenceAsync(CustomerReference(user.Id), cancellationToken);
            if (byReference is not null)
            {
                if (linked.MaxioCustomerId != byReference.Id)
                {
                    linked.MaxioCustomerId = byReference.Id;
                    linked.UpdatedAt = DateTimeOffset.UtcNow;
                    await _identityDb.SaveChangesAsync(cancellationToken);
                }
                return byReference;
            }
        }

        var customer = await _maxio.FindCustomerByReferenceAsync(CustomerReference(user.Id), cancellationToken);
        if (customer is not null)
        {
            await SaveCustomerLinkAsync(user.Id, customer.Id, cancellationToken);
        }
        return customer;
    }

    private async Task SaveCustomerLinkAsync(string userId, int customerId, CancellationToken cancellationToken)
    {
        var link = await _identityDb.MaxioCustomerLinks.FindAsync(new object[] { userId }, cancellationToken);
        if (link is null)
        {
            _identityDb.MaxioCustomerLinks.Add(new MaxioCustomerLink { UserId = userId, MaxioCustomerId = customerId, UpdatedAt = DateTimeOffset.UtcNow });
        }
        else
        {
            link.MaxioCustomerId = customerId;
            link.UpdatedAt = DateTimeOffset.UtcNow;
        }
        await _identityDb.SaveChangesAsync(cancellationToken);
    }

    private async Task SaveEnrollmentAsync(string userId, string productHandle, int subscriptionId, CancellationToken cancellationToken)
    {
        var enrollment = await _identityDb.MaxioSubscriptionEnrollments
            .SingleOrDefaultAsync(x => x.UserId == userId && x.ProductHandle == productHandle, cancellationToken);
        if (enrollment is null)
        {
            _identityDb.MaxioSubscriptionEnrollments.Add(new MaxioSubscriptionEnrollment
            {
                UserId = userId,
                ProductHandle = productHandle,
                MaxioSubscriptionId = subscriptionId,
                CreatedAt = DateTimeOffset.UtcNow
            });
            await _identityDb.SaveChangesAsync(cancellationToken);
        }
    }

    private static string CustomerReference(string userId) => $"eshop-user-{userId}";
    private static string SubscriptionReference(string userId, string productHandle) => $"eshop-subscription-{userId}-{productHandle}";

    private static SubscriptionPlanResponse ToPlanResponse(MaxioProduct product) => new()
    {
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? string.Empty,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? string.Empty
    };

    private static SubscriptionResponse ToSubscriptionResponse(MaxioSubscription subscription, MaxioProduct? fallbackPlan) => new()
    {
        Id = subscription.Id,
        PlanHandle = subscription.Product?.Handle ?? fallbackPlan?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? fallbackPlan?.Name ?? string.Empty,
        PriceInCents = subscription.ProductPriceInCents != 0 ? subscription.ProductPriceInCents : fallbackPlan?.PriceInCents ?? 0,
        Interval = subscription.Product?.Interval ?? fallbackPlan?.Interval ?? 0,
        IntervalUnit = subscription.Product?.IntervalUnit ?? fallbackPlan?.IntervalUnit ?? string.Empty,
        State = subscription.State ?? string.Empty,
        NextBillingAt = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt
    };
}

public sealed class SubscriptionValidationException : Exception
{
    public SubscriptionValidationException(string message) : base(message) { }
}
