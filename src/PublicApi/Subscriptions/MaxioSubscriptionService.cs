using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioSubscriptionService
{
    private const string CustomerReferencePrefix = "eshop-user:";
    private const string SubscriptionReferencePrefix = "eshop-subscription:";
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscriptionGates = new();

    private readonly IMaxioClient _maxioClient;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly CatalogContext _catalogContext;
    private readonly MaxioOptions _maxioOptions;

    public MaxioSubscriptionService(
        IMaxioClient maxioClient,
        UserManager<ApplicationUser> userManager,
        CatalogContext catalogContext,
        IOptions<MaxioOptions> maxioOptions)
    {
        _maxioClient = maxioClient;
        _userManager = userManager;
        _catalogContext = catalogContext;
        _maxioOptions = maxioOptions.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
    {
        var products = await _maxioClient.ListProductsAsync(GetProductFamilyHandle(), cancellationToken);
        return products
            .Where(product => product.ArchivedAt is null && !string.IsNullOrWhiteSpace(product.Handle))
            .Select(ToPlanDto)
            .ToArray();
    }

    public async Task<SubscriptionDto> SubscribeAsync(
        ClaimsPrincipal principal,
        string planHandle,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new SubscriptionPlanNotFoundException(planHandle);
        }

        var userName = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new InvalidOperationException("The authenticated user has no name claim.");
        }

        var user = await _userManager.FindByNameAsync(userName);
        if (user is null)
        {
            throw new InvalidOperationException($"The authenticated user '{userName}' was not found.");
        }

        var gate = SubscriptionGates.GetOrAdd(user.Id, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var product = (await _maxioClient.ListProductsAsync(GetProductFamilyHandle(), cancellationToken))
                .SingleOrDefault(candidate => string.Equals(candidate.Handle, planHandle, StringComparison.OrdinalIgnoreCase)
                    && candidate.ArchivedAt is null);
            if (product is null || string.IsNullOrWhiteSpace(product.Handle))
            {
                throw new SubscriptionPlanNotFoundException(planHandle);
            }

            var customerReference = CustomerReferencePrefix + user.Id;
            var customer = await EnsureCustomerAsync(user, customerReference, cancellationToken);
            var subscriptionReference = SubscriptionReferencePrefix + user.Id;

            // The reference is the idempotency key in Maxio. Looking it up before creating
            // also makes a retry safe after a successful Maxio call but a failed local save.
            var existing = await _maxioClient.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
            if (existing is not null)
            {
                EnsureSamePlan(existing, product.Handle);
                await SaveMappingAsync(user.Id, customer.Id, existing, product.Handle);
                return ToSubscriptionDto(existing, product);
            }

            MaxioSubscription subscription;
            try
            {
                subscription = await _maxioClient.CreateSubscriptionAsync(
                    product.Handle,
                    customer.Id,
                    subscriptionReference,
                    cancellationToken);
            }
            catch (MaxioApiException exception) when (exception.StatusCode == 422)
            {
                // Maxio enforces unique subscription references. A concurrent request may
                // have won the create race, so resolve that winner before failing the call.
                var concurrentSubscription = await _maxioClient.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
                if (concurrentSubscription is null)
                {
                    throw;
                }

                subscription = concurrentSubscription;
            }

            EnsureSamePlan(subscription, product.Handle);
            await SaveMappingAsync(user.Id, customer.Id, subscription, product.Handle);
            return ToSubscriptionDto(subscription, product);
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
        var userName = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new InvalidOperationException("The authenticated user has no name claim.");
        }

        var user = await _userManager.FindByNameAsync(userName);
        if (user is null)
        {
            throw new InvalidOperationException($"The authenticated user '{userName}' was not found.");
        }

        var customer = await _maxioClient.FindCustomerByReferenceAsync(CustomerReferencePrefix + user.Id, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var subscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(subscription => ToSubscriptionDto(subscription, subscription.Product)).ToArray();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(
        ApplicationUser user,
        string reference,
        CancellationToken cancellationToken)
    {
        var existing = await _maxioClient.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var email = user.Email ?? user.UserName;
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new InvalidOperationException("The authenticated user has no email address.");
        }

        try
        {
            return await _maxioClient.CreateCustomerAsync(
                user.UserName ?? "eShop",
                "Shopper",
                email,
                reference,
                cancellationToken);
        }
        catch (MaxioApiException exception) when (exception.StatusCode == 422)
        {
            // Maxio's customer reference is unique. Resolve a concurrent creator.
            var concurrentCustomer = await _maxioClient.FindCustomerByReferenceAsync(reference, cancellationToken);
            if (concurrentCustomer is null)
            {
                throw;
            }

            return concurrentCustomer;
        }
    }

    private async Task SaveMappingAsync(
        string userId,
        int customerId,
        MaxioSubscription subscription,
        string productHandle)
    {
        var now = DateTime.UtcNow;
        var mapping = await _catalogContext.SubscriptionMappings
            .SingleOrDefaultAsync(item => item.UserId == userId);

        if (mapping is null)
        {
            mapping = new SubscriptionMapping
            {
                UserId = userId,
                CreatedAtUtc = now
            };
            _catalogContext.SubscriptionMappings.Add(mapping);
        }

        mapping.MaxioCustomerId = customerId;
        mapping.MaxioSubscriptionId = subscription.Id;
        mapping.SubscriptionReference = subscription.Reference ?? SubscriptionReferencePrefix + userId;
        mapping.ProductHandle = productHandle;
        mapping.UpdatedAtUtc = now;

        try
        {
            await _catalogContext.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // A concurrent request may have persisted the same unique user mapping.
            // Maxio has already completed the authoritative operation, so the request
            // remains successful; the winning mapping is retained.
            _catalogContext.ChangeTracker.Clear();
        }
    }

    private string GetProductFamilyHandle()
    {
        // The configured handle is the only catalog selector; seeded numeric IDs are
        // intentionally not used.
        if (string.IsNullOrWhiteSpace(_maxioOptions.ProductFamilyHandle))
        {
            throw new InvalidOperationException("Maxio:ProductFamilyHandle is required.");
        }

        return _maxioOptions.ProductFamilyHandle;
    }

    private static void EnsureSamePlan(MaxioSubscription subscription, string requestedPlanHandle)
    {
        var existingPlan = subscription.Product?.Handle;
        if (!string.IsNullOrWhiteSpace(existingPlan)
            && !string.Equals(existingPlan, requestedPlanHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new SubscriptionConflictException("The user already has a subscription to a different plan.");
        }
    }

    private static SubscriptionPlanDto ToPlanDto(MaxioProduct product) => new()
    {
        Handle = product.Handle!,
        Name = product.Name ?? product.Handle!,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? string.Empty,
        RequiresPaymentMethod = product.RequireCreditCard
    };

    private static SubscriptionDto ToSubscriptionDto(MaxioSubscription subscription, MaxioProduct? product) => new()
    {
        Id = subscription.Id,
        PlanHandle = product?.Handle ?? string.Empty,
        PlanName = product?.Name ?? string.Empty,
        PriceInCents = subscription.ProductPriceInCents != 0
            ? subscription.ProductPriceInCents
            : product?.PriceInCents ?? 0,
        State = subscription.State ?? string.Empty,
        NextBillingDate = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt
    };
}
