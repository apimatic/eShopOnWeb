using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public class SubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly SemaphoreSlim[] OperationLocks =
        Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();
    private readonly IMaxioBillingClient _maxio;
    private readonly ISubscriptionLinkStore _links;
    private readonly MaxioOptions _options;

    public SubscriptionBillingService(
        IMaxioBillingClient maxio,
        ISubscriptionLinkStore links,
        MaxioOptions options)
    {
        _maxio = maxio;
        _links = links;
        _options = options;
    }

    public Task<IReadOnlyList<MaxioPlan>> GetPlansAsync(CancellationToken cancellationToken)
    {
        return _maxio.GetProductsAsync(_options.ProductFamilyHandle, cancellationToken);
    }

    public async Task<SubscribeResult> SubscribeAsync(
        BillingUser user,
        string productHandle,
        CancellationToken cancellationToken)
    {
        productHandle = productHandle.Trim();
        if (productHandle.Length is 0 or > 255)
        {
            throw new SubscriptionRequestException("A valid productHandle is required.");
        }

        var operationKey = $"{user.Id}\n{productHandle}";
        var lockIndex = (int)((uint)StringComparer.Ordinal.GetHashCode(operationKey) % (uint)OperationLocks.Length);
        var operationLock = OperationLocks[lockIndex];
        await operationLock.WaitAsync(cancellationToken);
        try
        {
            var plans = await GetPlansAsync(cancellationToken);
            var plan = plans.SingleOrDefault(item =>
                string.Equals(item.Handle, productHandle, StringComparison.Ordinal));
            if (plan is null)
            {
                throw new SubscriptionRequestException(
                    "The selected product is not an available subscription plan.");
            }

            if (plan.RequiresPaymentMethod)
            {
                throw new SubscriptionRequestException(
                    "The selected product requires a payment method and cannot be enrolled by this endpoint.");
            }

            var customerReference = StableReference("customer", user.Id);
            var subscriptionReference = StableReference("subscription", user.Id, productHandle);

            var existingSubscription = await _maxio.FindSubscriptionAsync(
                subscriptionReference,
                cancellationToken);
            if (existingSubscription is not null)
            {
                ValidateSubscription(existingSubscription, customerReference, productHandle);
                await SaveLinkAsync(user.Id, existingSubscription, cancellationToken);
                return new SubscribeResult(ToView(existingSubscription), Created: false);
            }

            var customer = await EnsureCustomerAsync(user, customerReference, cancellationToken);
            var site = await _maxio.GetSiteAsync(cancellationToken);
            var paymentCollectionMethod = site.RelationshipInvoicingEnabled ? "remittance" : "invoice";
            MaxioSubscription subscription;
            try
            {
                subscription = await _maxio.CreateSubscriptionAsync(
                    new CreateMaxioSubscription(
                        productHandle,
                        customerReference,
                        subscriptionReference,
                        paymentCollectionMethod,
                        StableGuid("subscription-remittance-v1", user.Id, productHandle)),
                    cancellationToken);
            }
            catch (Exception createError) when (createError is MaxioApiException or HttpRequestException or TaskCanceledException)
            {
                MaxioSubscription? recovered = null;
                try
                {
                    recovered = await _maxio.FindSubscriptionAsync(subscriptionReference, cancellationToken);
                }
                catch (Exception lookupError) when (lookupError is MaxioApiException or HttpRequestException or TaskCanceledException)
                {
                    // Preserve the original create failure, which is more actionable.
                }

                if (recovered is null)
                {
                    throw;
                }

                subscription = recovered;
            }

            ValidateSubscription(subscription, customerReference, productHandle);
            if (subscription.CustomerId != customer.Id)
            {
                throw new InvalidOperationException("Maxio returned a subscription for an unexpected customer.");
            }

            await SaveLinkAsync(user.Id, subscription, cancellationToken);
            return new SubscribeResult(ToView(subscription), Created: true);
        }
        finally
        {
            operationLock.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionView>> GetSubscriptionsAsync(
        BillingUser user,
        CancellationToken cancellationToken)
    {
        var customerReference = StableReference("customer", user.Id);
        var customer = await _maxio.FindCustomerAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionView>();
        }

        var subscriptions = await _maxio.GetCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions
            .Where(subscription => subscription.CustomerReference == customerReference)
            .Select(ToView)
            .OrderBy(subscription => subscription.ProductName)
            .ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(
        BillingUser user,
        string customerReference,
        CancellationToken cancellationToken)
    {
        var existing = await _maxio.FindCustomerAsync(customerReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var firstName = user.Email.Split('@', 2)[0];
        try
        {
            return await _maxio.CreateCustomerAsync(
                new CreateMaxioCustomer(
                    firstName,
                    "eShopOnWeb",
                    user.Email,
                    customerReference,
                    StableGuid("customer", user.Id)),
                cancellationToken);
        }
        catch (Exception createError) when (createError is MaxioApiException or HttpRequestException or TaskCanceledException)
        {
            MaxioCustomer? recovered = null;
            try
            {
                recovered = await _maxio.FindCustomerAsync(customerReference, cancellationToken);
            }
            catch (Exception lookupError) when (lookupError is MaxioApiException or HttpRequestException or TaskCanceledException)
            {
                // Preserve the original create failure, which is more actionable.
            }

            if (recovered is null)
            {
                throw;
            }

            return recovered;
        }
    }

    private async Task SaveLinkAsync(
        string userId,
        MaxioSubscription subscription,
        CancellationToken cancellationToken)
    {
        var link = await _links.FindAsync(userId, subscription.ProductHandle, cancellationToken);
        if (link is null)
        {
            link = new SubscriptionLink(
                userId,
                subscription.ProductHandle,
                subscription.CustomerId,
                subscription.Id,
                subscription.CustomerReference,
                subscription.Reference,
                DateTimeOffset.UtcNow);
        }
        else
        {
            link.Refresh(subscription.CustomerId, subscription.Id, DateTimeOffset.UtcNow);
        }

        await _links.SaveAsync(link, cancellationToken);
    }

    private static void ValidateSubscription(
        MaxioSubscription subscription,
        string expectedCustomerReference,
        string expectedProductHandle)
    {
        if (subscription.CustomerReference != expectedCustomerReference ||
            subscription.ProductHandle != expectedProductHandle)
        {
            throw new InvalidOperationException("The Maxio subscription reference resolved to unexpected data.");
        }
    }

    private static SubscriptionView ToView(MaxioSubscription subscription) => new(
        subscription.Id,
        subscription.ProductName,
        subscription.ProductHandle,
        subscription.PriceInCents,
        subscription.Interval,
        subscription.IntervalUnit,
        subscription.State,
        subscription.NextBillingAt);

    private static string StableReference(string scope, params string[] values)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(scope + "\n" + string.Join("\n", values)));
        return "eshop-" + Convert.ToHexString(bytes)[..32].ToLowerInvariant();
    }

    private static Guid StableGuid(string scope, params string[] values)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(scope + "\n" + string.Join("\n", values)));
        return new Guid(bytes.AsSpan(0, 16));
    }
}
