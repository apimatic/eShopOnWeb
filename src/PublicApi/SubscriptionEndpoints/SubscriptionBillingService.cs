using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken);
    Task<SubscribeResult?> SubscribeAsync(ApplicationUser user, string productHandle, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionDto>> GetSubscriptionsAsync(ApplicationUser user, CancellationToken cancellationToken);
}

public sealed class SubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscriptionLocks = new();
    private readonly IMaxioClient _maxioClient;
    private readonly MaxioOptions _options;

    public SubscriptionBillingService(IMaxioClient maxioClient, IOptions<MaxioOptions> options)
    {
        _maxioClient = maxioClient;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
    {
        var products = await _maxioClient.GetProductsAsync(_options.ProductFamilyHandle, cancellationToken);
        return products
            .Where(product => product.ArchivedAt is null && !string.IsNullOrWhiteSpace(product.Handle))
            .Select(MapPlan)
            .OrderBy(plan => plan.PriceInCents)
            .ToList();
    }

    public async Task<SubscribeResult?> SubscribeAsync(
        ApplicationUser user,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var normalizedHandle = productHandle.Trim();
        var products = await _maxioClient.GetProductsAsync(_options.ProductFamilyHandle, cancellationToken);
        var product = products.FirstOrDefault(candidate =>
            candidate.ArchivedAt is null &&
            string.Equals(candidate.Handle, normalizedHandle, StringComparison.OrdinalIgnoreCase));

        if (product?.Handle is null)
        {
            return null;
        }

        var lockKey = $"{user.Id}:{product.Handle}";
        var gate = SubscriptionLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customerReference = CreateReference("customer", user.Id);
            var subscriptionReference = CreateReference("subscription", user.Id, product.Handle);
            var existing = await _maxioClient.FindSubscriptionAsync(subscriptionReference, cancellationToken);
            if (existing is not null)
            {
                return new SubscribeResult(MapSubscription(existing), false);
            }

            await EnsureCustomerAsync(user, customerReference, cancellationToken);
            var site = await _maxioClient.GetSiteAsync(cancellationToken);
            var paymentCollectionMethod = site.RelationshipInvoicingEnabled ? "remittance" : "invoice";

            try
            {
                var created = await _maxioClient.CreateSubscriptionAsync(
                    new MaxioSubscriptionCreate(
                        product.Handle,
                        customerReference,
                        subscriptionReference,
                        paymentCollectionMethod),
                    CreateUniquenessToken("subscription", user.Id, product.Handle),
                    cancellationToken);
                return new SubscribeResult(MapSubscription(created), true);
            }
            catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
            {
                var reconciled = await ReconcileSubscriptionAsync(subscriptionReference, cancellationToken);
                if (reconciled is not null)
                {
                    return new SubscribeResult(MapSubscription(reconciled), false);
                }

                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetSubscriptionsAsync(
        ApplicationUser user,
        CancellationToken cancellationToken)
    {
        var customerReference = CreateReference("customer", user.Id);
        var customer = await _maxioClient.FindCustomerAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var subscriptions = await _maxioClient.GetCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(MapSubscription).OrderByDescending(item => item.Id).ToList();
    }

    private async Task EnsureCustomerAsync(
        ApplicationUser user,
        string customerReference,
        CancellationToken cancellationToken)
    {
        if (await _maxioClient.FindCustomerAsync(customerReference, cancellationToken) is not null)
        {
            return;
        }

        var email = user.Email ?? user.UserName
            ?? throw new InvalidOperationException("The authenticated user does not have an email address.");
        var firstName = email.Split('@', 2)[0];

        try
        {
            await _maxioClient.CreateCustomerAsync(
                new MaxioCustomerCreate(firstName, "eShopOnWeb", email, customerReference),
                CreateUniquenessToken("customer", user.Id),
                cancellationToken);
        }
        catch (MaxioApiException ex) when (
            ex.StatusCode == HttpStatusCode.Conflict || ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            if (await _maxioClient.FindCustomerAsync(customerReference, cancellationToken) is null)
            {
                throw;
            }
        }
    }

    private async Task<MaxioSubscription?> ReconcileSubscriptionAsync(
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        var delays = new[] { 100, 250, 500, 1000 };
        foreach (var delay in delays)
        {
            var subscription = await _maxioClient.FindSubscriptionAsync(subscriptionReference, cancellationToken);
            if (subscription is not null)
            {
                return subscription;
            }

            await Task.Delay(delay, cancellationToken);
        }

        return await _maxioClient.FindSubscriptionAsync(subscriptionReference, cancellationToken);
    }

    private static string CreateReference(string purpose, params string[] values)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes($"eshop:{purpose}:{string.Join(':', values)}"));
        return $"eshop-{purpose[..Math.Min(purpose.Length, 4)]}-{Convert.ToHexString(digest)[..32].ToLowerInvariant()}";
    }

    private static string CreateUniquenessToken(string purpose, params string[] values)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes($"eshop:maxio:{purpose}:{string.Join(':', values)}"));
        return new Guid(digest.AsSpan(0, 16)).ToString();
    }

    private static SubscriptionPlanDto MapPlan(MaxioProduct product) => new(
        product.Handle!,
        product.Name,
        product.Description,
        product.PriceInCents,
        product.Interval,
        product.IntervalUnit,
        product.RequireCreditCard);

    private static SubscriptionDto MapSubscription(MaxioSubscription subscription)
    {
        var product = subscription.Product;
        return new SubscriptionDto(
            subscription.Id,
            product?.Handle ?? string.Empty,
            product?.Name ?? string.Empty,
            subscription.ProductPriceInCents,
            product?.Interval ?? 0,
            product?.IntervalUnit ?? string.Empty,
            subscription.State,
            subscription.CurrentPeriodEndsAt);
    }
}

public sealed record SubscribeResult(SubscriptionDto Subscription, bool Created);

public sealed record SubscriptionPlanDto(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    bool PaymentMethodRequired);

public sealed record SubscriptionDto(
    long Id,
    string ProductHandle,
    string ProductName,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    string State,
    DateTimeOffset? NextBillingDate);
