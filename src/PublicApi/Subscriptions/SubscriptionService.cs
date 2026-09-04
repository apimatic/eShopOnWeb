using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public interface ISubscriptionService
{
    Task<SubscriptionPlansResponse> GetPlansAsync(CancellationToken cancellationToken);
    Task<MySubscriptionDto> SubscribeAsync(ClaimsPrincipal principal, string planHandle, CancellationToken cancellationToken);
    Task<MySubscriptionsResponse> GetMySubscriptionsAsync(ClaimsPrincipal principal, CancellationToken cancellationToken);
}

public sealed class SubscriptionService : ISubscriptionService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserLocks = new();
    private readonly IMaxioBillingClient _maxio;
    private readonly MaxioOptions _options;
    private readonly CatalogContext _catalogContext;
    private readonly UserManager<ApplicationUser> _userManager;

    public SubscriptionService(
        IMaxioBillingClient maxio,
        IOptions<MaxioOptions> options,
        CatalogContext catalogContext,
        UserManager<ApplicationUser> userManager)
    {
        _maxio = maxio;
        _options = options.Value;
        _catalogContext = catalogContext;
        _userManager = userManager;
    }

    public async Task<SubscriptionPlansResponse> GetPlansAsync(CancellationToken cancellationToken)
    {
        var products = await _maxio.ListProductsAsync(_options.ProductFamilyHandle, cancellationToken);
        return new SubscriptionPlansResponse
        {
            Plans = products.Select(ToPlanDto).ToArray()
        };
    }

    public async Task<MySubscriptionDto> SubscribeAsync(ClaimsPrincipal principal, string planHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new ArgumentException("A plan handle is required.", nameof(planHandle));
        }

        var user = await GetUserAsync(principal);
        var products = await _maxio.ListProductsAsync(_options.ProductFamilyHandle, cancellationToken);
        var product = products.SingleOrDefault(item => string.Equals(item.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
        if (product is null)
        {
            throw new PlanNotFoundException(planHandle);
        }

        var lockKey = $"{user.Id}:{product.Handle}";
        var gate = UserLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customerReference = GetCustomerReference(user.Id);
            var customer = await EnsureCustomerAsync(user, customerReference, cancellationToken);
            var subscriptionReference = GetSubscriptionReference(user.Id, product.Handle);
            var subscription = await _maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);

            if (subscription is null)
            {
                try
                {
                    subscription = await _maxio.CreateSubscriptionAsync(
                        product.Handle,
                        customerReference,
                        subscriptionReference,
                        cancellationToken);
                }
                catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict || ex.StatusCode == HttpStatusCode.UnprocessableEntity)
                {
                    // A second process may have won the reference race. Re-read the
                    // idempotency key before surfacing the error.
                    subscription = await _maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
                    if (subscription is null)
                    {
                        throw;
                    }
                }
            }

            await UpsertMappingAsync(user.Id, customer.Id, product, subscription, subscriptionReference, cancellationToken);
            return ToSubscriptionDto(subscription, product, subscriptionReference);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<MySubscriptionsResponse> GetMySubscriptionsAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var user = await GetUserAsync(principal);
        var customerReference = GetCustomerReference(user.Id);
        var customer = await _maxio.FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            return new MySubscriptionsResponse();
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var products = await _maxio.ListProductsAsync(_options.ProductFamilyHandle, cancellationToken);
        var productsByHandle = products.ToDictionary(product => product.Handle, StringComparer.OrdinalIgnoreCase);
        var result = new List<MySubscriptionDto>();

        foreach (var subscription in subscriptions)
        {
            var product = subscription.Product;
            if (product is null || product.ProductFamily is null ||
                !string.Equals(product.ProductFamily.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            productsByHandle.TryGetValue(product.Handle, out var currentProduct);
            var reference = subscription.Reference ?? GetSubscriptionReference(user.Id, product.Handle);
            await UpsertMappingAsync(user.Id, customer.Id, currentProduct ?? product, subscription, reference, cancellationToken);
            result.Add(ToSubscriptionDto(subscription, currentProduct ?? product, reference));
        }

        return new MySubscriptionsResponse { Subscriptions = result };
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(ApplicationUser user, string reference, CancellationToken cancellationToken)
    {
        var customer = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (customer is not null)
        {
            return customer;
        }

        var (firstName, lastName) = SplitName(user.UserName ?? user.Email ?? "Shopper");
        try
        {
            return await _maxio.CreateCustomerAsync(firstName, lastName, user.Email ?? user.UserName ?? reference, reference, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict || ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            customer = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
            if (customer is null)
            {
                throw;
            }

            return customer;
        }
    }

    private async Task UpsertMappingAsync(
        string userId,
        int customerId,
        MaxioProduct product,
        MaxioSubscription subscription,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        var mapping = await _catalogContext.MaxioSubscriptionMappings
            .SingleOrDefaultAsync(item => item.UserId == userId && item.MaxioSubscriptionId == subscription.Id, cancellationToken);

        if (mapping is null)
        {
            mapping = new MaxioSubscriptionMapping
            {
                UserId = userId,
                MaxioSubscriptionId = subscription.Id
            };
            await _catalogContext.MaxioSubscriptionMappings.AddAsync(mapping, cancellationToken);
        }

        mapping.MaxioCustomerId = customerId;
        mapping.PlanHandle = product.Handle;
        mapping.SubscriptionReference = subscriptionReference;
        mapping.LastSeenAt = DateTimeOffset.UtcNow;
        try
        {
            await _catalogContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            // The Maxio reference protects the external operation across app
            // instances. A concurrent instance can still win the local unique
            // mapping insert, so reload that row and make this request converge.
            _catalogContext.ChangeTracker.Clear();
            var existing = await _catalogContext.MaxioSubscriptionMappings
                .SingleOrDefaultAsync(item => item.UserId == userId && item.MaxioSubscriptionId == subscription.Id, cancellationToken);
            if (existing is null)
            {
                throw new DbUpdateException("Unable to persist the Maxio subscription mapping.", exception);
            }

            existing.MaxioCustomerId = customerId;
            existing.PlanHandle = product.Handle;
            existing.SubscriptionReference = subscriptionReference;
            existing.LastSeenAt = DateTimeOffset.UtcNow;
            await _catalogContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<ApplicationUser> GetUserAsync(ClaimsPrincipal principal)
    {
        var username = principal.FindFirstValue(ClaimTypes.Name) ?? principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new InvalidOperationException("The authenticated token does not contain a user identity.");
        }

        var user = await _userManager.FindByNameAsync(username);
        return user ?? throw new InvalidOperationException("The authenticated user no longer exists.");
    }

    private static SubscriptionPlanDto ToPlanDto(MaxioProduct product) => new()
    {
        Handle = product.Handle,
        Name = product.Name,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit,
        RequiresPaymentMethod = product.RequireCreditCard,
        Taxable = product.Taxable
    };

    private static MySubscriptionDto ToSubscriptionDto(MaxioSubscription subscription, MaxioProduct product, string reference) => new()
    {
        SubscriptionId = subscription.Id,
        Reference = reference,
        PlanHandle = product.Handle,
        PlanName = product.Name,
        PriceInCents = subscription.ProductPriceInCents > 0 ? subscription.ProductPriceInCents : product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit,
        State = subscription.State,
        NextBillingDate = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt
    };

    private static string GetCustomerReference(string userId) => $"eshop-user:{userId}";

    private static string GetSubscriptionReference(string userId, string planHandle) => $"eshop-subscription:{userId}:{planHandle}";

    private static (string FirstName, string LastName) SplitName(string value)
    {
        var parts = value.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 1 ? (parts[0], "Shopper") : (parts[0], parts[1]);
    }
}

public sealed class PlanNotFoundException : Exception
{
    public PlanNotFoundException(string planHandle)
        : base($"Subscription plan '{planHandle}' was not found in the configured Maxio product family.")
    {
    }
}
