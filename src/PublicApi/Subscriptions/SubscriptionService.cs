using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionService : ISubscriptionService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ProvisioningLocks = new();
    private readonly IMaxioClient _maxioClient;
    private readonly MaxioOptions _options;
    private readonly CatalogContext _catalogContext;

    public SubscriptionService(IMaxioClient maxioClient, IOptions<MaxioOptions> options, CatalogContext catalogContext)
    {
        _maxioClient = maxioClient;
        _options = options.Value;
        _catalogContext = catalogContext;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
    {
        var products = await _maxioClient.ListProductsAsync(RequiredProductFamilyHandle(), cancellationToken);
        return products
            .Where(product => product.ArchivedAt is null && !string.IsNullOrWhiteSpace(product.Handle))
            .Select(ToPlan)
            .ToArray();
    }

    public async Task<SubscriptionDto> SubscribeAsync(ApplicationUser user, string productHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
            throw new SubscriptionPlanNotFoundException(productHandle);

        var plans = await GetPlansAsync(cancellationToken);
        var plan = plans.SingleOrDefault(candidate => string.Equals(candidate.Handle, productHandle, StringComparison.Ordinal));
        if (plan is null)
            throw new SubscriptionPlanNotFoundException(productHandle);

        var customerReference = CustomerReference(user);
        var lockKey = $"{customerReference}:{productHandle}";
        var provisioningLock = ProvisioningLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await provisioningLock.WaitAsync(cancellationToken);
        try
        {
            var customer = await _maxioClient.FindCustomerByReferenceAsync(customerReference, cancellationToken);
            if (customer is null)
            {
                customer = await CreateCustomerWithRetryAsync(user, customerReference, cancellationToken);
            }

            var subscriptionReference = SubscriptionReference(customerReference, productHandle);
            var customerSubscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var subscription = customerSubscriptions.FirstOrDefault(candidate =>
                string.Equals(candidate.Reference, subscriptionReference, StringComparison.Ordinal));

            if (subscription is null)
            {
                subscription = await _maxioClient.CreateSubscriptionAsync(
                    productHandle,
                    customerReference,
                    subscriptionReference,
                    cancellationToken);
            }

            await SaveMappingAsync(user.Id, customer.Id, subscription, productHandle, subscriptionReference, cancellationToken);
            return ToSubscription(subscription, plan);
        }
        finally
        {
            provisioningLock.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var customerReference = CustomerReference(user);
        var customer = await _maxioClient.FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (customer is null)
            return Array.Empty<SubscriptionDto>();

        var subscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(subscription => ToSubscription(
            subscription,
            subscription.Product is null ? null : ToPlan(subscription.Product))).ToArray();
    }

    private async Task<MaxioCustomer> CreateCustomerWithRetryAsync(ApplicationUser user, string reference, CancellationToken cancellationToken)
    {
        var email = user.Email ?? user.UserName;
        if (string.IsNullOrWhiteSpace(email))
            throw new InvalidOperationException("The authenticated eShopOnWeb user does not have an email address.");

        var name = email.Split('@', 2)[0];
        var firstName = string.IsNullOrWhiteSpace(name) ? "eShopOnWeb" : name;
        try
        {
            return await _maxioClient.CreateCustomerAsync(reference, firstName, "Customer", email, cancellationToken);
        }
        catch (MaxioApiException exception) when (exception.StatusCode == 422)
        {
            // Customer reference is unique in the Maxio contract. A concurrent request may
            // have won the create race, so resolve it before surfacing the original error.
            var existing = await _maxioClient.FindCustomerByReferenceAsync(reference, cancellationToken);
            if (existing is not null)
                return existing;
            throw;
        }
    }

    private async Task SaveMappingAsync(string userId, int customerId, MaxioSubscription subscription, string productHandle, string reference, CancellationToken cancellationToken)
    {
        var mapping = await _catalogContext.SubscriptionMappings
            .SingleOrDefaultAsync(item => item.UserId == userId && item.ProductHandle == productHandle, cancellationToken);

        if (mapping is null)
        {
            _catalogContext.SubscriptionMappings.Add(new SubscriptionMapping
            {
                UserId = userId,
                MaxioCustomerId = customerId,
                MaxioSubscriptionId = subscription.Id,
                ProductHandle = productHandle,
                SubscriptionReference = reference,
                CreatedAtUtc = DateTime.UtcNow
            });
        }
        else
        {
            mapping.MaxioCustomerId = customerId;
            mapping.MaxioSubscriptionId = subscription.Id;
        }

        await _catalogContext.SaveChangesAsync(cancellationToken);
    }

    private string RequiredProductFamilyHandle()
    {
        if (string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
            throw new InvalidOperationException("Maxio:ProductFamilyHandle is required.");
        return _options.ProductFamilyHandle;
    }

    private static string CustomerReference(ApplicationUser user)
    {
        var stableIdentity = user.UserName ?? user.Email ?? user.Id;
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stableIdentity.ToUpperInvariant())))
            .ToLowerInvariant();
        return $"eshop:{digest}";
    }

    private static string SubscriptionReference(string customerReference, string productHandle) =>
        $"{customerReference}:{productHandle}";

    private static SubscriptionPlanDto ToPlan(MaxioProduct product) => new()
    {
        Handle = product.Handle!,
        Name = product.Name,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit,
        RequireCreditCard = product.RequireCreditCard,
        Taxable = product.Taxable
    };

    private static SubscriptionDto ToSubscription(MaxioSubscription subscription, SubscriptionPlanDto? plan)
    {
        var product = subscription.Product;
        return new SubscriptionDto
        {
            Id = subscription.Id,
            ProductHandle = product?.Handle ?? plan?.Handle ?? string.Empty,
            ProductName = product?.Name ?? plan?.Name ?? string.Empty,
            PriceInCents = subscription.ProductPriceInCents != 0
                ? subscription.ProductPriceInCents
                : product?.PriceInCents ?? plan?.PriceInCents ?? 0,
            Interval = product?.Interval ?? plan?.Interval ?? 0,
            IntervalUnit = product?.IntervalUnit ?? plan?.IntervalUnit ?? string.Empty,
            State = subscription.State,
            NextBillingDate = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
            Reference = subscription.Reference
        };
    }
}
