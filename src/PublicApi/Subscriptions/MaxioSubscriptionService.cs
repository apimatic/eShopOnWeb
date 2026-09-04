using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public interface IMaxioSubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken);

    Task<SubscriptionDto?> SubscribeAsync(string planHandle, CancellationToken cancellationToken);

    Task<IReadOnlyList<SubscriptionDto>?> GetMySubscriptionsAsync(CancellationToken cancellationToken);
}

internal sealed class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscriptionLocks = new(StringComparer.Ordinal);
    private readonly IMaxioBillingClient _billingClient;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AppIdentityDbContext _identityDbContext;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MaxioSubscriptionService(
        IMaxioBillingClient billingClient,
        UserManager<ApplicationUser> userManager,
        AppIdentityDbContext identityDbContext,
        IHttpContextAccessor httpContextAccessor)
    {
        _billingClient = billingClient;
        _userManager = userManager;
        _identityDbContext = identityDbContext;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
    {
        var products = await _billingClient.ListProductsAsync(cancellationToken);
        return products
            .Where(product => product.ArchivedAt is null)
            .Select(ToPlanDto)
            .ToArray();
    }

    public async Task<SubscriptionDto?> SubscribeAsync(string planHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
            throw new SubscriptionValidationException("PlanHandle is required.");

        var userName = _httpContextAccessor.HttpContext?.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
            return null;

        var user = await _userManager.FindByNameAsync(userName);
        if (user is null)
            return null;

        var products = await _billingClient.ListProductsAsync(cancellationToken);
        var product = products.FirstOrDefault(candidate =>
            candidate.ArchivedAt is null && string.Equals(candidate.Handle, planHandle, StringComparison.Ordinal));
        if (product is null)
            throw new SubscriptionPlanNotFoundException(planHandle);

        var customerReference = CreateCustomerReference(user.Id);
        var subscriptionReference = CreateSubscriptionReference(user.Id, product.Handle);
        var lockKey = $"{user.Id}:{product.Handle}";
        var gate = SubscriptionLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            // The Maxio reference lookup makes this safe across app instances and also
            // recovers a subscription created before the local mapping was saved.
            var existingSubscription = await _billingClient.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
            if (existingSubscription is not null)
            {
                await SaveMappingAsync(user.Id, customerId: await ResolveCustomerIdAsync(customerReference, existingSubscription, cancellationToken), product.Handle, existingSubscription, cancellationToken);
                return ToSubscriptionDto(existingSubscription, product);
            }

            var customer = await EnsureCustomerAsync(user, customerReference, cancellationToken);
            existingSubscription = await _billingClient.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
            var subscription = existingSubscription ?? await CreateSubscriptionRecoveringFromConflictAsync(
                product.Handle, customerReference, subscriptionReference, cancellationToken);

            await SaveMappingAsync(user.Id, customer.Id, product.Handle, subscription, cancellationToken);
            return ToSubscriptionDto(subscription, product);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>?> GetMySubscriptionsAsync(CancellationToken cancellationToken)
    {
        var userName = _httpContextAccessor.HttpContext?.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
            return null;

        var user = await _userManager.FindByNameAsync(userName);
        if (user is null)
            return null;

        var customer = await _billingClient.FindCustomerByReferenceAsync(CreateCustomerReference(user.Id), cancellationToken);
        if (customer is null)
            return Array.Empty<SubscriptionDto>();

        var products = await _billingClient.ListProductsAsync(cancellationToken);
        var productsByHandle = products.ToDictionary(product => product.Handle, StringComparer.Ordinal);
        var subscriptions = await _billingClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(subscription =>
        {
            var product = subscription.Product is not null && productsByHandle.TryGetValue(subscription.Product.Handle, out var listedProduct)
                ? listedProduct
                : null;
            return ToSubscriptionDto(subscription, product);
        }).ToArray();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(ApplicationUser user, string reference, CancellationToken cancellationToken)
    {
        var customer = await _billingClient.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (customer is not null)
            return customer;

        var email = user.Email ?? user.UserName ?? reference;
        var firstName = email.Split('@', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "eShopOnWeb";
        try
        {
            return await _billingClient.CreateCustomerAsync(reference, firstName, "Customer", email, cancellationToken);
        }
        catch (MaxioApiException exception) when ((int)exception.StatusCode is >= 400 and < 500)
        {
            // Maxio enforces reference uniqueness. A concurrent create can therefore
            // be safely converted into a lookup of the winner.
            customer = await _billingClient.FindCustomerByReferenceAsync(reference, cancellationToken);
            if (customer is not null)
                return customer;
            throw;
        }
    }

    private async Task<MaxioSubscription> CreateSubscriptionRecoveringFromConflictAsync(
        string productHandle,
        string customerReference,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _billingClient.CreateSubscriptionAsync(productHandle, customerReference, subscriptionReference, cancellationToken);
        }
        catch (MaxioApiException exception) when ((int)exception.StatusCode is >= 400 and < 500)
        {
            var subscription = await _billingClient.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
            if (subscription is not null)
                return subscription;
            throw;
        }
    }

    private async Task<long> ResolveCustomerIdAsync(string customerReference, MaxioSubscription subscription, CancellationToken cancellationToken)
    {
        var customer = await _billingClient.FindCustomerByReferenceAsync(customerReference, cancellationToken);
        return customer?.Id ?? throw new MaxioApiException(System.Net.HttpStatusCode.Conflict, "Maxio customer could not be resolved for the existing subscription.");
    }

    private async Task SaveMappingAsync(
        string userId,
        long customerId,
        string productHandle,
        MaxioSubscription subscription,
        CancellationToken cancellationToken)
    {
        var mapping = await _identityDbContext.MaxioSubscriptionMappings
            .SingleOrDefaultAsync(item => item.UserId == userId && item.ProductHandle == productHandle, cancellationToken);
        var now = DateTime.UtcNow;
        if (mapping is null)
        {
            _identityDbContext.MaxioSubscriptionMappings.Add(new MaxioSubscriptionMapping
            {
                UserId = userId,
                MaxioCustomerId = customerId,
                MaxioSubscriptionId = subscription.Id,
                ProductHandle = productHandle,
                SubscriptionReference = subscription.Reference,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
        }
        else
        {
            mapping.MaxioCustomerId = customerId;
            mapping.MaxioSubscriptionId = subscription.Id;
            mapping.SubscriptionReference = subscription.Reference;
            mapping.UpdatedAtUtc = now;
        }

        await _identityDbContext.SaveChangesAsync(cancellationToken);
    }

    private static SubscriptionPlanDto ToPlanDto(MaxioProduct product) => new()
    {
        Handle = product.Handle,
        Name = product.Name,
        Description = product.Description ?? string.Empty,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit
    };

    private static SubscriptionDto ToSubscriptionDto(MaxioSubscription subscription, MaxioProduct? product) => new()
    {
        Id = subscription.Id,
        PlanHandle = subscription.Product?.Handle ?? product?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? product?.Name ?? string.Empty,
        PriceInCents = subscription.ProductPriceInCents != 0 ? subscription.ProductPriceInCents : product?.PriceInCents ?? 0,
        State = subscription.State,
        NextBillingDate = subscription.NextAssessmentAt
    };

    private static string CreateCustomerReference(string userId) => $"eshoponweb:user:{userId}";

    private static string CreateSubscriptionReference(string userId, string productHandle) =>
        $"eshoponweb:user:{userId}:plan:{productHandle}";
}

public sealed class SubscriptionValidationException : Exception
{
    public SubscriptionValidationException(string message) : base(message)
    {
    }
}

public sealed class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string planHandle)
        : base($"Subscription plan '{planHandle}' was not found in the configured Maxio product family.")
    {
    }
}
