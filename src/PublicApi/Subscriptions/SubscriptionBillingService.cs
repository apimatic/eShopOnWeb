using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionBillingService
{
    private const string SubscriptionReferencePrefix = "eshop:";
    private readonly IMaxioBillingClient _maxio;
    private readonly AppIdentityDbContext _db;

    public SubscriptionBillingService(
        IMaxioBillingClient maxio,
        AppIdentityDbContext db)
    {
        _maxio = maxio;
        _db = db;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(MaxioOptions options, CancellationToken cancellationToken)
    {
        options.Validate();
        var products = await _maxio.ListProductsAsync(options.ProductFamilyHandle, cancellationToken);
        return products
            .Where(product => !string.IsNullOrWhiteSpace(product.Handle))
            .Select(ToPlan)
            .ToArray();
    }

    public async Task<SubscriptionDto?> SubscribeAsync(ApplicationUser user, string productHandle, MaxioOptions options, CancellationToken cancellationToken)
    {
        options.Validate();
        productHandle = productHandle.Trim();

        // Validate the handle against the live catalog so callers cannot subscribe to another family/site product.
        var plan = (await _maxio.ListProductsAsync(options.ProductFamilyHandle, cancellationToken))
            .SingleOrDefault(product => string.Equals(product.Handle, productHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
            return null;

        var customerReference = CustomerReference(user.Id);
        var subscriptionReference = SubscriptionReference(user.Id, plan.Handle);
        var mapping = await _db.MaxioSubscriptionMappings
            .SingleOrDefaultAsync(item => item.UserId == user.Id && item.ProductHandle == plan.Handle, cancellationToken);

        MaxioSubscription? subscription = null;
        if (mapping is not null)
        {
            subscription = await TryGetSubscriptionAsync(mapping.MaxioSubscriptionId, cancellationToken);
            if (subscription is null)
                subscription = await _maxio.FindSubscriptionByReferenceAsync(mapping.SubscriptionReference, cancellationToken);
        }

        // This also repairs a lost local mapping after an in-memory database restart or an interrupted write.
        subscription ??= await _maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);

        var customer = await GetOrCreateCustomerAsync(user, customerReference, cancellationToken);
        if (subscription is null)
        {
            try
            {
                subscription = await _maxio.CreateSubscriptionAsync(new MaxioCreateSubscription
                {
                    ProductHandle = plan.Handle,
                    CustomerReference = customerReference,
                    Reference = subscriptionReference,
                    UniquenessToken = UniquenessToken(user.Id, plan.Handle),
                    PaymentCollectionMethod = "remittance"
                }, cancellationToken);
            }
            catch (MaxioApiException ex) when (ex.StatusCode is 409 or 422)
            {
                // Maxio's documented duplicate-prevention response is 409. Resolve the original result by reference.
                subscription = await _maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
                if (subscription is null)
                    throw;
            }
        }

        // Creation can briefly return Maxio's internal pending state. Read the resource once
        // more so the caller receives the current state and next assessment date.
        subscription = await RefreshSubscriptionAsync(subscription, cancellationToken);

        await SaveMappingAsync(user.Id, customer.Id, plan.Handle, subscriptionReference, subscription, cancellationToken);
        return ToDto(subscription, plan);
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var customer = await _maxio.FindCustomerByReferenceAsync(CustomerReference(user.Id), cancellationToken);
        if (customer is null)
            return Array.Empty<SubscriptionDto>();

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var appSubscriptions = subscriptions
            .Where(subscription => subscription.Reference?.StartsWith(SubscriptionReferencePrefix + user.Id + ":", StringComparison.OrdinalIgnoreCase) == true)
            .ToArray();

        foreach (var subscription in appSubscriptions)
        {
            var handle = subscription.Product?.Handle ?? ProductHandleFromReference(subscription.Reference);
            if (handle is null) continue;
            await SaveMappingAsync(user.Id, customer.Id, handle, subscription.Reference!, subscription, cancellationToken);
        }

        return appSubscriptions.Select(subscription => ToDto(subscription, subscription.Product)).ToArray();
    }

    private async Task<MaxioCustomer> GetOrCreateCustomerAsync(ApplicationUser user, string reference, CancellationToken cancellationToken)
    {
        var existing = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null) return existing;

        var email = user.Email ?? user.UserName;
        if (string.IsNullOrWhiteSpace(email))
            throw new InvalidOperationException("The authenticated user does not have an email address for Maxio.");

        var name = (user.UserName ?? email).Split('@', 2)[0];
        try
        {
            return await _maxio.CreateCustomerAsync(new MaxioCreateCustomer
            {
                FirstName = name,
                LastName = "eShop customer",
                Email = email,
                Reference = reference
            }, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode is 409 or 422)
        {
            // A concurrent request may have won the unique customer-reference race.
            var customerAfterRace = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
            if (customerAfterRace is null)
                throw;
            return customerAfterRace;
        }
    }

    private async Task<MaxioSubscription?> TryGetSubscriptionAsync(long id, CancellationToken cancellationToken)
    {
        try
        {
            return await _maxio.GetSubscriptionAsync(id, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 404)
        {
            return null;
        }
    }

    private async Task<MaxioSubscription> RefreshSubscriptionAsync(MaxioSubscription subscription, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var current = await TryGetSubscriptionAsync(subscription.Id, cancellationToken);
            if (current is null)
                return subscription;

            subscription = current;
            if (!string.Equals(subscription.State, "pending", StringComparison.OrdinalIgnoreCase) ||
                subscription.NextAssessmentAt is not null ||
                subscription.CurrentPeriodEndsAt is not null)
                return subscription;

            if (attempt < 4)
                await Task.Delay(TimeSpan.FromMilliseconds(100 * (attempt + 1)), cancellationToken);
        }

        return subscription;
    }

    private async Task SaveMappingAsync(string userId, long customerId, string productHandle, string reference, MaxioSubscription subscription, CancellationToken cancellationToken)
    {
        var mapping = await _db.MaxioSubscriptionMappings
            .SingleOrDefaultAsync(item => item.UserId == userId && item.ProductHandle == productHandle, cancellationToken);
        if (mapping is null)
        {
            _db.MaxioSubscriptionMappings.Add(new MaxioSubscriptionMapping
            {
                UserId = userId,
                MaxioCustomerId = customerId,
                MaxioSubscriptionId = subscription.Id,
                SubscriptionReference = reference,
                ProductHandle = productHandle,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            mapping.MaxioCustomerId = customerId;
            mapping.MaxioSubscriptionId = subscription.Id;
            mapping.SubscriptionReference = reference;
        }

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Another request may have persisted the same unique mapping. Re-read it; if it exists, the operation succeeded.
            foreach (var addedMapping in _db.MaxioSubscriptionMappings.Local
                         .Where(mapping => _db.Entry(mapping).State == EntityState.Added)
                         .ToArray())
                _db.Entry(addedMapping).State = EntityState.Detached;
            var alreadySaved = await _db.MaxioSubscriptionMappings
                .AsNoTracking()
                .AnyAsync(item => item.UserId == userId && item.ProductHandle == productHandle && item.MaxioSubscriptionId == subscription.Id, cancellationToken);
            if (!alreadySaved) throw;
        }
    }

    private static SubscriptionPlanDto ToPlan(MaxioProduct product) => new()
    {
        Handle = product.Handle,
        Name = product.Name,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit
    };

    private static SubscriptionDto ToDto(MaxioSubscription subscription, MaxioProduct? plan) => new()
    {
        Id = subscription.Id,
        ProductHandle = plan?.Handle ?? string.Empty,
        ProductName = plan?.Name ?? string.Empty,
        PriceInCents = subscription.ProductPriceInCents ?? plan?.PriceInCents ?? 0,
        State = subscription.State,
        NextBillingDate = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt
    };

    private static string CustomerReference(string userId) => $"{SubscriptionReferencePrefix}customer:{userId}";
    private static string SubscriptionReference(string userId, string productHandle) => $"{SubscriptionReferencePrefix}{userId}:{productHandle}";
    private static string UniquenessToken(string userId, string productHandle)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(SubscriptionReference(userId, productHandle)));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string? ProductHandleFromReference(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) return null;
        var separator = reference.IndexOf(':', SubscriptionReferencePrefix.Length);
        return separator >= 0 && separator < reference.Length - 1 ? reference[(separator + 1)..] : null;
    }
}
