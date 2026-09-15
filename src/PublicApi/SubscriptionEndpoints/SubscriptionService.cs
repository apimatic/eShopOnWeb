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

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserLocks = new(StringComparer.Ordinal);

    private readonly IMaxioBillingClient _maxio;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AppIdentityDbContext _identityDb;
    public SubscriptionService(
        IMaxioBillingClient maxio,
        UserManager<ApplicationUser> userManager,
        AppIdentityDbContext identityDb)
    {
        _maxio = maxio;
        _userManager = userManager;
        _identityDb = identityDb;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
    {
        var products = await _maxio.GetPlansAsync(cancellationToken);
        return products
            .Where(product => !string.IsNullOrWhiteSpace(product.Handle) && product.ArchivedAt is null)
            .Select(ToPlanDto)
            .ToArray();
    }

    public async Task<SubscribeResult> SubscribeAsync(
        ClaimsPrincipal principal,
        string? requestedPlanHandle,
        CancellationToken cancellationToken)
    {
        var user = await ResolveUserAsync(principal);
        var plans = await _maxio.GetPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(item =>
            item.ArchivedAt is null &&
            !string.IsNullOrWhiteSpace(item.Handle) &&
            string.Equals(item.Handle, requestedPlanHandle?.Trim(), StringComparison.OrdinalIgnoreCase));

        if (plan?.Handle is null)
        {
            throw new SubscriptionPlanNotFoundException();
        }

        var productHandle = plan.Handle;
        var gate = UserLocks.GetOrAdd(user.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var subscriptionReference = BuildSubscriptionReference(user.Id, productHandle);
            var mapping = await _identityDb.SubscriptionMappings
                .SingleOrDefaultAsync(item => item.UserId == user.Id && item.ProductHandle == productHandle, cancellationToken);

            if (mapping is not null)
            {
                var existingSubscription = await _maxio.GetSubscriptionAsync(mapping.MaxioSubscriptionId, cancellationToken);
                if (existingSubscription is not null)
                {
                    return new SubscribeResult(ToSubscriptionDto(existingSubscription, productHandle), false);
                }

                _identityDb.SubscriptionMappings.Remove(mapping);
                await _identityDb.SaveChangesAsync(cancellationToken);
            }

            var customerReference = BuildCustomerReference(user.Id);
            var customer = await _maxio.GetCustomerByReferenceAsync(customerReference, cancellationToken);
            if (customer is null)
            {
                try
                {
                    customer = await _maxio.CreateCustomerAsync(
                        GetFirstName(user.Email ?? user.UserName ?? "Shopper"),
                        "Shopper",
                        user.Email ?? user.UserName ?? throw new InvalidOperationException("The authenticated user has no email address."),
                        customerReference,
                        cancellationToken);
                }
                catch (MaxioApiException exception) when ((int)exception.StatusCode is 400 or 409 or 422)
                {
                    // A second app instance may have won the create race. The unique Maxio
                    // customer reference is the authoritative way to recover that race.
                    customer = await _maxio.GetCustomerByReferenceAsync(customerReference, cancellationToken);
                    if (customer is null)
                    {
                        throw;
                    }
                }
            }

            var existingByReference = (await _maxio.GetCustomerSubscriptionsAsync(customer.Id, cancellationToken))
                .FirstOrDefault(item => string.Equals(item.Reference, subscriptionReference, StringComparison.Ordinal));
            if (existingByReference is not null)
            {
                await SaveMappingAsync(user.Id, customer.Id, productHandle, existingByReference, subscriptionReference, cancellationToken);
                return new SubscribeResult(ToSubscriptionDto(existingByReference, productHandle), false);
            }

            MaxioSubscription createdSubscription;
            try
            {
                createdSubscription = await _maxio.CreateSubscriptionAsync(
                    productHandle,
                    customer.Id,
                    subscriptionReference,
                    cancellationToken);
            }
            catch (MaxioApiException exception) when ((int)exception.StatusCode is 400 or 409 or 422)
            {
                // If another app instance created this exact subscription first, recover by
                // reference instead of surfacing a duplicate-signup error to the shopper.
                var concurrentSubscription = (await _maxio.GetCustomerSubscriptionsAsync(customer.Id, cancellationToken))
                    .FirstOrDefault(item => string.Equals(item.Reference, subscriptionReference, StringComparison.Ordinal));
                if (concurrentSubscription is null)
                {
                    throw;
                }

                await SaveMappingAsync(user.Id, customer.Id, productHandle, concurrentSubscription, subscriptionReference, cancellationToken);
                return new SubscribeResult(ToSubscriptionDto(concurrentSubscription, productHandle), false);
            }

            await SaveMappingAsync(user.Id, customer.Id, productHandle, createdSubscription, subscriptionReference, cancellationToken);
            return new SubscribeResult(ToSubscriptionDto(createdSubscription, productHandle), true);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var user = await ResolveUserAsync(principal);
        var mappings = await _identityDb.SubscriptionMappings
            .Where(item => item.UserId == user.Id)
            .ToListAsync(cancellationToken);

        var customerId = mappings.Select(item => (int?)item.MaxioCustomerId).FirstOrDefault();
        if (customerId is null)
        {
            var customer = await _maxio.GetCustomerByReferenceAsync(BuildCustomerReference(user.Id), cancellationToken);
            if (customer is null)
            {
                return Array.Empty<SubscriptionDto>();
            }

            customerId = customer.Id;
        }

        var subscriptions = await _maxio.GetCustomerSubscriptionsAsync(customerId.Value, cancellationToken);
        var planHandles = mappings.ToDictionary(item => item.MaxioSubscriptionId, item => item.ProductHandle);
        return subscriptions
            .Select(subscription => ToSubscriptionDto(
                subscription,
                planHandles.TryGetValue(subscription.Id, out var mappedHandle)
                    ? mappedHandle
                    : subscription.Product?.Handle ?? string.Empty))
            .ToArray();
    }

    private async Task<ApplicationUser> ResolveUserAsync(ClaimsPrincipal principal)
    {
        var userName = principal.FindFirstValue(ClaimTypes.Name) ?? principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new UnauthorizedAccessException("The access token does not identify a user.");
        }

        var user = await _userManager.FindByNameAsync(userName);
        return user ?? throw new UnauthorizedAccessException("The authenticated user no longer exists.");
    }

    private async Task SaveMappingAsync(
        string userId,
        int customerId,
        string productHandle,
        MaxioSubscription subscription,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        var mapping = await _identityDb.SubscriptionMappings
            .SingleOrDefaultAsync(item => item.UserId == userId && item.ProductHandle == productHandle, cancellationToken);

        if (mapping is null)
        {
            _identityDb.SubscriptionMappings.Add(new SubscriptionMapping
            {
                UserId = userId,
                MaxioCustomerId = customerId,
                MaxioSubscriptionId = subscription.Id,
                ProductHandle = productHandle,
                SubscriptionReference = subscriptionReference,
                CreatedAtUtc = DateTime.UtcNow
            });
        }
        else
        {
            mapping.MaxioCustomerId = customerId;
            mapping.MaxioSubscriptionId = subscription.Id;
            mapping.SubscriptionReference = subscriptionReference;
        }

        await _identityDb.SaveChangesAsync(cancellationToken);
    }

    private static SubscriptionPlanDto ToPlanDto(MaxioProduct product) => new()
    {
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? product.Handle ?? string.Empty,
        Description = product.Description ?? string.Empty,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? string.Empty,
        TrialInterval = product.TrialInterval,
        TrialIntervalUnit = product.TrialIntervalUnit
    };

    private static SubscriptionDto ToSubscriptionDto(MaxioSubscription subscription, string productHandle) => new()
    {
        Id = subscription.Id,
        PlanHandle = productHandle,
        PlanName = subscription.Product?.Name ?? productHandle,
        PriceInCents = subscription.PriceInCents != 0
            ? subscription.PriceInCents
            : subscription.Product?.PriceInCents ?? 0,
        State = subscription.State ?? string.Empty,
        NextBillingDate = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt
    };

    private static string BuildCustomerReference(string userId) => $"eshop-user:{userId}";

    private static string BuildSubscriptionReference(string userId, string productHandle)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(productHandle));
        var handleHash = Convert.ToHexString(bytes).ToLowerInvariant();
        return $"eshop-subscription:{userId}:{handleHash}";
    }

    private static string GetFirstName(string email)
    {
        var localPart = email.Split('@', 2)[0].Trim();
        return string.IsNullOrWhiteSpace(localPart) ? "Shopper" : localPart[..Math.Min(localPart.Length, 50)];
    }
}

public sealed class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException()
        : base("The requested subscription plan is not available.")
    {
    }
}
