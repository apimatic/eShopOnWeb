using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionBillingService : ISubscriptionBillingService
{
    private readonly IMaxioClient _maxioClient;
    private readonly CatalogContext _catalogContext;
    private readonly ISubscriptionOperationLock _operationLock;

    public SubscriptionBillingService(
        IMaxioClient maxioClient,
        CatalogContext catalogContext,
        ISubscriptionOperationLock operationLock)
    {
        _maxioClient = maxioClient;
        _catalogContext = catalogContext;
        _operationLock = operationLock;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var products = await _maxioClient.ListProductsAsync(cancellationToken);
        return products
            .Where(product => product.ArchivedAt is null && !string.IsNullOrWhiteSpace(product.Handle))
            .OrderBy(product => product.PriceInCents)
            .Select(ToPlanDto)
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(
        ApplicationUser user,
        string planHandle,
        CancellationToken cancellationToken)
    {
        var normalizedHandle = planHandle.Trim();
        var identityKey = GetIdentityKey(user);
        var customerReference = $"eshop-user-{identityKey}";
        var subscriptionReference = $"eshop-sub-{identityKey}-{Hash(normalizedHandle.ToLowerInvariant())}";

        using var operation = await _operationLock.AcquireAsync(subscriptionReference, cancellationToken);

        var existing = await _maxioClient.FindSubscriptionAsync(subscriptionReference, cancellationToken);
        if (existing is not null)
        {
            await ReconcileAsync(user.Id, customerReference, subscriptionReference, existing, cancellationToken);
            return new SubscribeResult(ToSubscriptionDto(existing), false);
        }

        var products = await _maxioClient.ListProductsAsync(cancellationToken);
        var product = products.SingleOrDefault(candidate =>
            candidate.ArchivedAt is null &&
            string.Equals(candidate.Handle, normalizedHandle, StringComparison.OrdinalIgnoreCase));
        if (product is null)
        {
            throw new SubscriptionPlanNotFoundException(normalizedHandle);
        }

        var customer = await EnsureCustomerAsync(user, customerReference, cancellationToken);
        MaxioSubscription subscription;
        try
        {
            subscription = await _maxioClient.CreateSubscriptionAsync(
                new MaxioCreateSubscription(product.Handle!, customerReference, subscriptionReference, "remittance"),
                cancellationToken);
        }
        catch (MaxioApiException)
        {
            // A timed-out or raced create may still have committed in Maxio. Reconcile by
            // deterministic reference before reporting the request as failed.
            var reconciled = await _maxioClient.FindSubscriptionAsync(subscriptionReference, cancellationToken);
            if (reconciled is null)
            {
                throw;
            }

            subscription = reconciled;
        }

        await ReconcileAsync(user.Id, customerReference, subscriptionReference, subscription, cancellationToken, customer.Id);
        return new SubscribeResult(ToSubscriptionDto(subscription), true);
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListForUserAsync(
        ApplicationUser user,
        CancellationToken cancellationToken)
    {
        var customerReference = $"eshop-user-{GetIdentityKey(user)}";
        var customer = await _maxioClient.FindCustomerAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var subscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        foreach (var subscription in subscriptions)
        {
            var planHandle = subscription.Product.Handle ?? $"product-{subscription.Product.Id}";
            var reference = subscription.Reference ??
                $"eshop-sub-{GetIdentityKey(user)}-{Hash(planHandle.ToLowerInvariant())}";
            await ReconcileAsync(
                user.Id,
                customerReference,
                reference,
                subscription,
                cancellationToken,
                customer.Id,
                saveChanges: false);
        }

        await _catalogContext.SaveChangesAsync(cancellationToken);
        return subscriptions.Select(ToSubscriptionDto).ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(
        ApplicationUser user,
        string customerReference,
        CancellationToken cancellationToken)
    {
        var existing = await _maxioClient.FindCustomerAsync(customerReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var email = user.Email ?? user.UserName ?? throw new InvalidOperationException("The authenticated user has no email address.");
        var localPart = email.Split('@', 2)[0].Replace('.', ' ').Replace('_', ' ').Trim();
        var firstName = string.IsNullOrWhiteSpace(localPart) ? "eShop" : localPart;

        try
        {
            return await _maxioClient.CreateCustomerAsync(
                new MaxioCreateCustomer(firstName, "Customer", email, customerReference),
                cancellationToken);
        }
        catch (MaxioApiException)
        {
            var reconciled = await _maxioClient.FindCustomerAsync(customerReference, cancellationToken);
            if (reconciled is null)
            {
                throw;
            }

            return reconciled;
        }
    }

    private async Task ReconcileAsync(
        string userId,
        string customerReference,
        string subscriptionReference,
        MaxioSubscription subscription,
        CancellationToken cancellationToken,
        int? customerId = null,
        bool saveChanges = true)
    {
        var planHandle = subscription.Product.Handle ?? $"product-{subscription.Product.Id}";
        var record = await _catalogContext.SubscriptionRecords.SingleOrDefaultAsync(
            candidate => candidate.UserId == userId && candidate.PlanHandle == planHandle,
            cancellationToken);

        if (record is null)
        {
            record = new SubscriptionRecord(
                userId,
                planHandle,
                customerId ?? subscription.Customer.Id,
                subscription.Id,
                customerReference,
                subscriptionReference);
            await _catalogContext.SubscriptionRecords.AddAsync(record, cancellationToken);
        }

        record.Reconcile(
            customerId ?? subscription.Customer.Id,
            subscription.Id,
            subscription.Product.Name,
            subscription.ProductPriceInCents,
            subscription.State,
            subscription.Currency ?? string.Empty,
            subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
            DateTimeOffset.UtcNow);

        if (saveChanges)
        {
            await _catalogContext.SaveChangesAsync(cancellationToken);
        }
    }

    private static string GetIdentityKey(ApplicationUser user)
    {
        var stableIdentity = user.NormalizedEmail ?? user.NormalizedUserName ?? user.Id;
        return Hash(stableIdentity.ToLowerInvariant());
    }

    private static string Hash(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..32];
    }

    private static SubscriptionPlanDto ToPlanDto(MaxioProduct product)
    {
        return new SubscriptionPlanDto(
            product.Handle!,
            product.Name,
            product.Description,
            product.PriceInCents,
            product.Interval,
            product.IntervalUnit,
            product.RequireCreditCard);
    }

    private static SubscriptionDto ToSubscriptionDto(MaxioSubscription subscription)
    {
        return new SubscriptionDto(
            subscription.Id,
            subscription.Product.Handle ?? $"product-{subscription.Product.Id}",
            subscription.Product.Name,
            subscription.ProductPriceInCents,
            subscription.Currency ?? string.Empty,
            subscription.State,
            subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt);
    }
}
