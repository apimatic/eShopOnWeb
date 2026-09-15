using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscriptionLocks = new();
    private readonly IMaxioBillingClient _maxio;
    private readonly AppIdentityDbContext _identityDb;
    private readonly MaxioOptions _options;

    public SubscriptionService(
        IMaxioBillingClient maxio,
        AppIdentityDbContext identityDb,
        IOptions<MaxioOptions> options)
    {
        _maxio = maxio;
        _identityDb = identityDb;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
    {
        var products = await _maxio.ListProductsAsync(_options.ProductFamilyHandle, cancellationToken);
        return products.Select(MapPlan).ToArray();
    }

    public async Task<SubscriptionDto> SubscribeAsync(
        ApplicationUser user,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var products = await _maxio.ListProductsAsync(_options.ProductFamilyHandle, cancellationToken);
        var product = products.FirstOrDefault(item => string.Equals(item.Handle, productHandle, StringComparison.OrdinalIgnoreCase));
        if (product is null)
        {
            throw new SubscriptionPlanNotFoundException();
        }

        var normalizedHandle = product.Handle.ToLowerInvariant();
        var lockKey = $"{user.Id}:{normalizedHandle}";
        var subscriptionLock = SubscriptionLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await subscriptionLock.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(user, cancellationToken);
            var reference = BuildSubscriptionReference(user.Id, normalizedHandle);
            var mapping = await _identityDb.MaxioSubscriptionMappings
                .SingleOrDefaultAsync(item => item.UserId == user.Id && item.ProductHandle == normalizedHandle, cancellationToken);

            MaxioSubscription? subscription = null;
            if (mapping?.MaxioSubscriptionId is long mappedSubscriptionId)
            {
                subscription = await _maxio.GetSubscriptionAsync(mappedSubscriptionId, cancellationToken);
            }

            subscription ??= await _maxio.FindSubscriptionByReferenceAsync(reference, cancellationToken);
            if (subscription is null)
            {
                subscription = await _maxio.CreateSubscriptionAsync(new MaxioCreateSubscription
                {
                    ProductHandle = product.Handle,
                    CustomerId = customer.Id,
                    Reference = reference,
                    PaymentCollectionMethod = "remittance"
                }, cancellationToken);
            }

            if (mapping is null)
            {
                mapping = new MaxioSubscriptionMapping
                {
                    UserId = user.Id,
                    MaxioCustomerId = customer.Id,
                    ProductHandle = normalizedHandle,
                    SubscriptionReference = reference,
                    CreatedAtUtc = DateTime.UtcNow
                };
                _identityDb.MaxioSubscriptionMappings.Add(mapping);
            }

            mapping.MaxioCustomerId = customer.Id;
            mapping.MaxioSubscriptionId = subscription.Id;
            mapping.State = subscription.State;
            mapping.UpdatedAtUtc = DateTime.UtcNow;
            await _identityDb.SaveChangesAsync(cancellationToken);

            return MapSubscription(subscription, product);
        }
        finally
        {
            subscriptionLock.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(
        ApplicationUser user,
        CancellationToken cancellationToken)
    {
        var customer = await FindCustomerAsync(user, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var products = await _maxio.ListProductsAsync(_options.ProductFamilyHandle, cancellationToken);
        var productByHandle = products.ToDictionary(item => item.Handle, StringComparer.OrdinalIgnoreCase);

        foreach (var subscription in subscriptions)
        {
            if (string.IsNullOrWhiteSpace(subscription.Reference))
            {
                continue;
            }

            var mapping = await _identityDb.MaxioSubscriptionMappings
                .SingleOrDefaultAsync(item => item.SubscriptionReference == subscription.Reference, cancellationToken);
            if (mapping is null)
            {
                continue;
            }

            mapping.MaxioSubscriptionId = subscription.Id;
            mapping.MaxioCustomerId = customer.Id;
            mapping.State = subscription.State;
            mapping.UpdatedAtUtc = DateTime.UtcNow;
        }

        await _identityDb.SaveChangesAsync(cancellationToken);

        return subscriptions.Select(subscription =>
        {
            var product = subscription.Product is not null && productByHandle.TryGetValue(subscription.Product.Handle, out var catalogProduct)
                ? catalogProduct
                : subscription.Product;
            return MapSubscription(subscription, product);
        }).ToArray();
    }

    private async Task<MaxioCustomer?> FindCustomerAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var mapping = await _identityDb.MaxioCustomerMappings
            .SingleOrDefaultAsync(item => item.UserId == user.Id, cancellationToken);
        if (mapping is not null)
        {
            return new MaxioCustomer
            {
                Id = mapping.MaxioCustomerId,
                Reference = mapping.CustomerReference,
                Email = user.Email ?? user.UserName ?? string.Empty
            };
        }

        return await _maxio.FindCustomerByReferenceAsync(BuildCustomerReference(user.Id), cancellationToken);
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var reference = BuildCustomerReference(user.Id);
        var mapping = await _identityDb.MaxioCustomerMappings
            .SingleOrDefaultAsync(item => item.UserId == user.Id, cancellationToken);
        if (mapping is not null)
        {
            return new MaxioCustomer
            {
                Id = mapping.MaxioCustomerId,
                Reference = mapping.CustomerReference,
                Email = user.Email ?? user.UserName ?? string.Empty
            };
        }

        var customer = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (customer is null)
        {
            try
            {
                customer = await _maxio.CreateCustomerAsync(new MaxioCreateCustomer
                {
                    FirstName = "eShop",
                    LastName = "Customer",
                    Email = user.Email ?? user.UserName ?? $"{user.Id}@invalid.local",
                    Reference = reference
                }, cancellationToken);
            }
            catch (MaxioApiException exception) when (exception.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                // A concurrent request may have won the unique customer-reference race.
                var existingCustomer = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
                if (existingCustomer is null)
                {
                    throw;
                }

                customer = existingCustomer;
            }
        }

        mapping = new MaxioCustomerMapping
        {
            UserId = user.Id,
            MaxioCustomerId = customer.Id,
            CustomerReference = reference,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        _identityDb.MaxioCustomerMappings.Add(mapping);
        try
        {
            await _identityDb.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _identityDb.Entry(mapping).State = EntityState.Detached;
            var existingMapping = await _identityDb.MaxioCustomerMappings
                .SingleOrDefaultAsync(item => item.UserId == user.Id, cancellationToken);
            if (existingMapping is null)
            {
                throw;
            }

            customer.Id = existingMapping.MaxioCustomerId;
        }

        return customer;
    }

    private static string BuildCustomerReference(string userId) => $"eshop-user:{userId}";

    private static string BuildSubscriptionReference(string userId, string productHandle) =>
        $"eshop-subscription:{userId}:{productHandle}";

    private static SubscriptionPlanDto MapPlan(MaxioProduct product) => new()
    {
        Handle = product.Handle,
        Name = product.Name,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit,
        // The seeded plans intentionally request a card on the hosted flow but
        // do not require one. The required flag is the value relevant to this API flow.
        RequiresPaymentMethod = product.RequireCreditCard == true,
        Taxable = product.Taxable == true
    };

    private static SubscriptionDto MapSubscription(MaxioSubscription subscription, MaxioProduct? product) => new()
    {
        Id = subscription.Id,
        CustomerId = subscription.CustomerId != 0 ? subscription.CustomerId : subscription.Customer?.Id ?? 0,
        ProductHandle = product?.Handle ?? string.Empty,
        PlanName = product?.Name ?? string.Empty,
        PriceInCents = product?.PriceInCents ?? subscription.ProductPriceInCents,
        Interval = product?.Interval,
        IntervalUnit = product?.IntervalUnit,
        State = subscription.State,
        NextBillingDate = subscription.NextAssessmentAt
    };
}

public sealed class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException() : base("The requested subscription plan is not available.")
    {
    }
}
