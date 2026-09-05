using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Coordinates local retry records with Maxio, which remains the billing system of record.
/// </summary>
public sealed class SubscriptionEnrollmentService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> EnrollmentLocks = new();
    private readonly MaxioBillingClient _maxio;
    private readonly AppIdentityDbContext _identityContext;
    private readonly UserManager<ApplicationUser> _userManager;

    public SubscriptionEnrollmentService(MaxioBillingClient maxio, AppIdentityDbContext identityContext,
        UserManager<ApplicationUser> userManager)
    {
        _maxio = maxio;
        _identityContext = identityContext;
        _userManager = userManager;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
    {
        var products = await _maxio.GetPlansAsync(cancellationToken);
        return products
            .Where(product => product.Handle is not null)
            .Select(ToPlanDto)
            .OrderBy(plan => plan.Price)
            .ThenBy(plan => plan.Name, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<SubscriptionDto> SubscribeAsync(string userName, string productHandle,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new SubscriptionValidationException("ProductHandle is required.");
        }

        var user = await FindUserAsync(userName);
        var plans = await _maxio.GetPlansAsync(cancellationToken);
        var plan = plans.SingleOrDefault(product => string.Equals(product.Handle, productHandle,
            StringComparison.OrdinalIgnoreCase));
        if (plan?.Handle is null)
        {
            throw new SubscriptionValidationException("The requested subscription plan is not available.");
        }

        // The database's unique index and a process lock make double-clicks safe. The durable
        // uniqueness token is also reused if a process fails after Maxio accepted the request.
        var lockKey = $"{user.Id}:{plan.Handle}";
        var gate = EnrollmentLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(user, cancellationToken);
            var enrollment = await GetOrCreateEnrollmentAsync(user.Id, plan.Handle, cancellationToken);
            var subscriptions = await _maxio.GetCustomerSubscriptionsAsync(customer.Id, cancellationToken);

            var existing = subscriptions.FirstOrDefault(subscription =>
                subscription.Product?.Handle is not null &&
                string.Equals(subscription.Product.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                await RecordSubscriptionAsync(enrollment, existing.Id, cancellationToken);
                return ToSubscriptionDto(existing);
            }

            MaxioSubscription created;
            try
            {
                created = await _maxio.CreateSubscriptionAsync(customer.Id, plan.Handle, enrollment.UniquenessToken,
                    cancellationToken);
            }
            catch (MaxioApiException exception) when (exception.StatusCode == HttpStatusCode.Conflict)
            {
                // The documented duplicate-prevention response can follow an interrupted retry.
                // Re-read Maxio before reporting a failure, because it is the source of truth.
                subscriptions = await _maxio.GetCustomerSubscriptionsAsync(customer.Id, cancellationToken);
                var recovered = subscriptions.FirstOrDefault(subscription =>
                    subscription.Product?.Handle is not null &&
                    string.Equals(subscription.Product.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase));
                if (recovered is null)
                {
                    throw;
                }
                created = recovered;
            }

            await RecordSubscriptionAsync(enrollment, created.Id, cancellationToken);
            return ToSubscriptionDto(created);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(string userName,
        CancellationToken cancellationToken)
    {
        var user = await FindUserAsync(userName);
        var reference = CustomerReference(user.Id);
        var customer = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        await UpsertCustomerCorrelationAsync(user.Id, customer.Id, cancellationToken);
        var subscriptions = await _maxio.GetCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(ToSubscriptionDto).ToList();
    }

    private async Task<ApplicationUser> FindUserAsync(string userName)
    {
        var user = await _userManager.FindByNameAsync(userName);
        return user ?? throw new SubscriptionValidationException("The authenticated user no longer exists.");
    }

    private async Task<Microsoft.eShopWeb.PublicApi.Maxio.MaxioCustomer> EnsureCustomerAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var reference = CustomerReference(user.Id);
        var customer = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (customer is null)
        {
            var email = user.Email ?? user.UserName;
            if (string.IsNullOrWhiteSpace(email))
            {
                throw new SubscriptionValidationException("The authenticated user needs an email address before subscribing.");
            }

            try
            {
                customer = await _maxio.CreateCustomerAsync(new MaxioCustomerCreate
                {
                    // eShopOnWeb has no profile-name fields. These required Maxio fields are safe
                    // placeholders until the storefront has a customer-profile feature.
                    FirstName = "eShop",
                    LastName = "Shopper",
                    Email = email,
                    Reference = reference
                }, Guid.NewGuid().ToString("D"), cancellationToken);
            }
            catch (MaxioApiException exception) when (exception.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                customer = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
                if (customer is null)
                {
                    throw;
                }
            }
        }

        await UpsertCustomerCorrelationAsync(user.Id, customer.Id, cancellationToken);
        return customer;
    }

    private async Task<MaxioSubscriptionEnrollment> GetOrCreateEnrollmentAsync(string userId, string productHandle,
        CancellationToken cancellationToken)
    {
        var existing = await _identityContext.MaxioSubscriptionEnrollments
            .SingleOrDefaultAsync(x => x.UserId == userId && x.ProductHandle == productHandle, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var now = DateTimeOffset.UtcNow;
        var enrollment = new MaxioSubscriptionEnrollment
        {
            UserId = userId,
            ProductHandle = productHandle,
            UniquenessToken = Guid.NewGuid().ToString("D"),
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
            // A second app instance won the unique-index race. Use its durable idempotency token.
            _identityContext.Entry(enrollment).State = EntityState.Detached;
            return await _identityContext.MaxioSubscriptionEnrollments.SingleAsync(
                x => x.UserId == userId && x.ProductHandle == productHandle, cancellationToken);
        }
    }

    private async Task UpsertCustomerCorrelationAsync(string userId, int maxioCustomerId,
        CancellationToken cancellationToken)
    {
        var correlation = await _identityContext.MaxioCustomers.SingleOrDefaultAsync(x => x.UserId == userId,
            cancellationToken);
        var now = DateTimeOffset.UtcNow;
        if (correlation is null)
        {
            _identityContext.MaxioCustomers.Add(new Microsoft.eShopWeb.Infrastructure.Identity.MaxioCustomer
            {
                UserId = userId,
                MaxioCustomerId = maxioCustomerId,
                CreatedAt = now,
                UpdatedAt = now
            });
        }
        else
        {
            correlation.MaxioCustomerId = maxioCustomerId;
            correlation.UpdatedAt = now;
        }

        await _identityContext.SaveChangesAsync(cancellationToken);
    }

    private async Task RecordSubscriptionAsync(MaxioSubscriptionEnrollment enrollment, long maxioSubscriptionId,
        CancellationToken cancellationToken)
    {
        enrollment.MaxioSubscriptionId = maxioSubscriptionId;
        enrollment.UpdatedAt = DateTimeOffset.UtcNow;
        await _identityContext.SaveChangesAsync(cancellationToken);
    }

    private static string CustomerReference(string userId) => $"eshop-user-{userId}";

    private static SubscriptionPlanDto ToPlanDto(MaxioProduct product) => new()
    {
        Handle = product.Handle!,
        Name = product.Name,
        Description = product.Description,
        Price = product.PriceInCents / 100m,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit
    };

    private static SubscriptionDto ToSubscriptionDto(MaxioSubscription subscription) => new()
    {
        Id = subscription.Id,
        PlanHandle = subscription.Product?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? "Subscription",
        Price = subscription.ProductPriceInCents / 100m,
        State = subscription.State,
        NextBillingAt = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt
    };
}

public sealed class SubscriptionValidationException : Exception
{
    public SubscriptionValidationException(string message) : base(message)
    {
    }
}
