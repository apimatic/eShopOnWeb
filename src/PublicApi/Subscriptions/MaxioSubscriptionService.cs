using System;
using System.Collections.Concurrent;
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

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public interface ISubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken);
    Task<SubscriptionDto> SubscribeAsync(string userName, string planHandle, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(string userName, CancellationToken cancellationToken);
}

public sealed class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string planHandle)
        : base($"Subscription plan '{planHandle}' was not found in the configured Maxio product family.")
    {
    }
}

public sealed class MaxioSubscriptionService : ISubscriptionService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscriptionLocks = new();

    private readonly IMaxioBillingClient _maxio;
    private readonly AppIdentityDbContext _identityDb;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly MaxioOptions _options;

    public MaxioSubscriptionService(
        IMaxioBillingClient maxio,
        AppIdentityDbContext identityDb,
        UserManager<ApplicationUser> userManager,
        Microsoft.Extensions.Options.IOptions<MaxioOptions> options)
    {
        _maxio = maxio;
        _identityDb = identityDb;
        _userManager = userManager;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var products = await _maxio.ListPlansAsync(cancellationToken);
        return products.Select(product => new SubscriptionPlanDto
        {
            Handle = product.Handle,
            Name = product.Name,
            Description = product.Description,
            PriceInCents = product.PriceInCents,
            Interval = product.Interval,
            IntervalUnit = product.IntervalUnit,
            RequiresPaymentMethod = product.RequireCreditCard,
            Taxable = product.Taxable
        }).ToArray();
    }

    public async Task<SubscriptionDto> SubscribeAsync(string userName, string planHandle, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByNameAsync(userName) ?? throw new InvalidOperationException("The authenticated eShopOnWeb user could not be found.");
        var products = await _maxio.ListPlansAsync(cancellationToken);
        var product = products.FirstOrDefault(item => string.Equals(item.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
        if (product is null)
        {
            throw new SubscriptionPlanNotFoundException(planHandle);
        }

        var customerReference = BuildCustomerReference(user.Id);
        var subscriptionReference = BuildSubscriptionReference(user.Id, product.Handle);
        var lockKey = $"{user.Id}:{product.Handle.ToUpperInvariant()}";
        var subscriptionLock = SubscriptionLocks.GetOrAdd(lockKey, static _ => new SemaphoreSlim(1, 1));

        await subscriptionLock.WaitAsync(cancellationToken);
        try
        {
            var mapping = await _identityDb.MaxioSubscriptionMappings
                .SingleOrDefaultAsync(item => item.UserId == user.Id && item.ProductHandle == product.Handle, cancellationToken);

            if (mapping is not null)
            {
                var mappedSubscription = await _maxio.GetSubscriptionAsync(mapping.MaxioSubscriptionId, cancellationToken);
                if (mappedSubscription is not null)
                {
                    return ToDto(mappedSubscription, product);
                }
            }

            // This also repairs the local mapping after a successful Maxio call followed by
            // a process crash, so retrying the same request cannot create another signup.
            var existingSubscription = await _maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
            if (existingSubscription is not null)
            {
                await SaveMappingAsync(user.Id, customerReference, existingSubscription, product.Handle, cancellationToken);
                return ToDto(existingSubscription, product);
            }

            var customer = await EnsureCustomerAsync(user, customerReference, cancellationToken);
            MaxioSubscription createdSubscription;
            try
            {
                createdSubscription = await _maxio.CreateSubscriptionAsync(product.Handle, subscriptionReference, customer.Id, cancellationToken);
            }
            catch (MaxioApiException exception) when (exception.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                // A concurrent worker may have won the create race. Maxio's reference lookup
                // is the authoritative way to recover the already-created subscription.
                existingSubscription = await _maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
                if (existingSubscription is null)
                {
                    throw;
                }

                await SaveMappingAsync(user.Id, customerReference, existingSubscription, product.Handle, cancellationToken);
                return ToDto(existingSubscription, product);
            }

            await SaveMappingAsync(user.Id, customerReference, createdSubscription, product.Handle, cancellationToken);
            return ToDto(createdSubscription, product);
        }
        finally
        {
            subscriptionLock.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(string userName, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByNameAsync(userName) ?? throw new InvalidOperationException("The authenticated eShopOnWeb user could not be found.");
        var customerReference = BuildCustomerReference(user.Id);
        var customer = await _maxio.FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions
            .Where(subscription => subscription.Product?.ProductFamily?.Handle is null ||
                string.Equals(subscription.Product.ProductFamily.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
            .Select(subscription => ToDto(subscription, subscription.Product))
            .ToArray();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(ApplicationUser user, string reference, CancellationToken cancellationToken)
    {
        var customer = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (customer is not null)
        {
            return customer;
        }

        var email = user.Email ?? user.UserName ?? $"{user.Id}@invalid.local";
        var localPart = email.Split('@', 2)[0];
        var firstName = string.IsNullOrWhiteSpace(localPart) ? "eShopOnWeb" : localPart;
        try
        {
            return await _maxio.CreateCustomerAsync(reference, firstName, "Customer", email, cancellationToken);
        }
        catch (MaxioApiException exception) when (exception.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Customer references are unique in Maxio. If another request created it after
            // our lookup, use that record rather than creating a second customer.
            customer = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
            if (customer is null)
            {
                throw;
            }

            return customer;
        }
    }

    private async Task SaveMappingAsync(string userId, string customerReference, MaxioSubscription subscription, string productHandle, CancellationToken cancellationToken)
    {
        var mapping = await _identityDb.MaxioSubscriptionMappings
            .SingleOrDefaultAsync(item => item.UserId == userId && item.ProductHandle == productHandle, cancellationToken);

        if (mapping is null)
        {
            _identityDb.MaxioSubscriptionMappings.Add(new MaxioSubscriptionMapping
            {
                UserId = userId,
                CustomerReference = customerReference,
                MaxioCustomerId = subscription.Customer?.Id ?? 0,
                SubscriptionReference = BuildSubscriptionReference(userId, productHandle),
                MaxioSubscriptionId = subscription.Id,
                ProductHandle = productHandle,
                CreatedAtUtc = DateTime.UtcNow
            });
        }
        else
        {
            mapping.MaxioCustomerId = subscription.Customer?.Id ?? mapping.MaxioCustomerId;
            mapping.MaxioSubscriptionId = subscription.Id;
        }

        await _identityDb.SaveChangesAsync(cancellationToken);
    }

    private static SubscriptionDto ToDto(MaxioSubscription subscription, MaxioProduct? fallbackProduct)
    {
        var product = subscription.Product ?? fallbackProduct;
        return new SubscriptionDto
        {
            Id = subscription.Id,
            PlanHandle = product?.Handle ?? string.Empty,
            PlanName = product?.Name ?? string.Empty,
            PriceInCents = subscription.ProductPriceInCents != 0 ? subscription.ProductPriceInCents : product?.PriceInCents ?? 0,
            State = subscription.State,
            NextBillingDate = subscription.CurrentPeriodEndsAt
        };
    }

    private static string BuildCustomerReference(string userId) => $"eshop-user-{userId}";

    private static string BuildSubscriptionReference(string userId, string productHandle)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(productHandle))).ToLowerInvariant();
        return $"eshop-subscription-{userId}-{hash}";
    }
}
