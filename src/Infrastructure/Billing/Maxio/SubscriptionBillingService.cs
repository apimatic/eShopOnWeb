using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

public sealed class SubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscriptionLocks = new();

    private readonly IMaxioClient _maxioClient;
    private readonly CatalogContext _catalogContext;

    public SubscriptionBillingService(IMaxioClient maxioClient, CatalogContext catalogContext)
    {
        _maxioClient = maxioClient;
        _catalogContext = catalogContext;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _maxioClient.GetProductsAsync(cancellationToken);
        return products
            .Select(ToPlan)
            .OrderBy(plan => plan.PriceInCents)
            .ThenBy(plan => plan.Name, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<SubscriptionEnrollment> SubscribeAsync(
        ShopperBillingIdentity shopper,
        string productHandle,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new SubscriptionValidationException("A productHandle is required.");
        }

        if (productHandle.Length > 255)
        {
            throw new SubscriptionValidationException("The productHandle is too long.");
        }

        var subscriptionLock = SubscriptionLocks.GetOrAdd(
            $"{shopper.UserId}\n{productHandle}",
            _ => new SemaphoreSlim(1, 1));
        await subscriptionLock.WaitAsync(cancellationToken);

        try
        {
            var products = await _maxioClient.GetProductsAsync(cancellationToken);
            var product = products.SingleOrDefault(candidate =>
                string.Equals(candidate.Handle, productHandle, StringComparison.Ordinal));
            if (product is null)
            {
                throw new SubscriptionValidationException(
                    $"Product handle '{productHandle}' is not an available subscription plan.");
            }

            var customerReference = MaxioReferenceGenerator.CustomerReference(shopper.UserId);
            var subscriptionReference = MaxioReferenceGenerator.SubscriptionReference(shopper.UserId, productHandle);
            var record = await GetOrCreateEnrollmentRecordAsync(
                shopper.UserId,
                customerReference,
                subscriptionReference,
                product,
                cancellationToken);

            var subscription = await _maxioClient.FindSubscriptionAsync(subscriptionReference, cancellationToken);
            var created = false;
            if (subscription is null)
            {
                var customer = await EnsureCustomerAsync(shopper, customerReference, cancellationToken);
                var creation = await CreateOrRecoverSubscriptionAsync(
                    productHandle,
                    customer.Reference,
                    subscriptionReference,
                    record,
                    cancellationToken);
                subscription = creation.Subscription;
                created = creation.Created;
            }

            var summary = ToSummary(subscription, product);
            await UpsertSnapshotAsync(
                shopper.UserId,
                customerReference,
                subscriptionReference,
                subscription,
                summary,
                cancellationToken);
            return new SubscriptionEnrollment(summary, created);
        }
        finally
        {
            subscriptionLock.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionSummary>> GetSubscriptionsAsync(
        ShopperBillingIdentity shopper,
        CancellationToken cancellationToken = default)
    {
        var customerReference = MaxioReferenceGenerator.CustomerReference(shopper.UserId);
        var customer = await _maxioClient.FindCustomerAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionSummary>();
        }

        var products = await _maxioClient.GetProductsAsync(cancellationToken);
        var productsByHandle = products.ToDictionary(product => product.Handle, StringComparer.Ordinal);
        var subscriptions = await _maxioClient.GetCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var summaries = new List<SubscriptionSummary>();

        foreach (var subscription in subscriptions)
        {
            if (subscription.Product is null ||
                !productsByHandle.TryGetValue(subscription.Product.Handle, out var product))
            {
                continue;
            }

            var summary = ToSummary(subscription, product);
            var subscriptionReference = subscription.Reference ??
                MaxioReferenceGenerator.SubscriptionReference(shopper.UserId, product.Handle);
            await UpsertSnapshotAsync(
                shopper.UserId,
                customerReference,
                subscriptionReference,
                subscription,
                summary,
                cancellationToken);
            summaries.Add(summary);
        }

        return summaries
            .OrderBy(summary => summary.ProductName, StringComparer.Ordinal)
            .ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(
        ShopperBillingIdentity shopper,
        string customerReference,
        CancellationToken cancellationToken)
    {
        var customer = await _maxioClient.FindCustomerAsync(customerReference, cancellationToken);
        if (customer is not null)
        {
            return customer;
        }

        try
        {
            return await _maxioClient.CreateCustomerAsync(
                new MaxioCreateCustomer(
                    Truncate(shopper.FirstName, 100),
                    Truncate(shopper.LastName, 100),
                    shopper.Email,
                    customerReference,
                    Guid.NewGuid().ToString()),
                cancellationToken);
        }
        catch (MaxioApiException exception) when (
            exception.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity)
        {
            customer = await _maxioClient.FindCustomerAsync(customerReference, cancellationToken);
            if (customer is null)
            {
                throw;
            }

            return customer;
        }
    }

    private async Task<MaxioSubscriptionCreation> CreateOrRecoverSubscriptionAsync(
        string productHandle,
        string customerReference,
        string subscriptionReference,
        SubscriptionRecord record,
        CancellationToken cancellationToken)
    {
        try
        {
            var subscription = await _maxioClient.CreateSubscriptionAsync(
                new MaxioCreateSubscription(
                    productHandle,
                    customerReference,
                    subscriptionReference,
                    record.SubscriptionUniquenessToken),
                cancellationToken);
            return new MaxioSubscriptionCreation(subscription, true);
        }
        catch (MaxioApiException exception) when (
            exception.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity)
        {
            var subscription = await _maxioClient.FindSubscriptionAsync(subscriptionReference, cancellationToken);
            if (subscription is null)
            {
                record.RotateSubscriptionUniquenessToken(Guid.NewGuid().ToString());
                await _catalogContext.SaveChangesAsync(cancellationToken);
                throw;
            }

            return new MaxioSubscriptionCreation(subscription, false);
        }
    }

    private async Task<SubscriptionRecord> GetOrCreateEnrollmentRecordAsync(
        string userId,
        string customerReference,
        string subscriptionReference,
        MaxioProduct product,
        CancellationToken cancellationToken)
    {
        var record = await _catalogContext.SubscriptionRecords.SingleOrDefaultAsync(
            candidate => candidate.UserId == userId && candidate.ProductHandle == product.Handle,
            cancellationToken);
        if (record is not null)
        {
            return record;
        }

        record = new SubscriptionRecord(
            userId,
            product.Handle,
            customerReference,
            subscriptionReference,
            Guid.NewGuid().ToString(),
            product.Name,
            product.PriceInCents,
            product.Interval,
            product.IntervalUnit);
        _catalogContext.SubscriptionRecords.Add(record);

        try
        {
            await _catalogContext.SaveChangesAsync(cancellationToken);
            return record;
        }
        catch (DbUpdateException)
        {
            _catalogContext.ChangeTracker.Clear();
            return await _catalogContext.SubscriptionRecords.SingleAsync(
                candidate => candidate.UserId == userId && candidate.ProductHandle == product.Handle,
                cancellationToken);
        }
    }

    private async Task UpsertSnapshotAsync(
        string userId,
        string customerReference,
        string subscriptionReference,
        MaxioSubscription subscription,
        SubscriptionSummary summary,
        CancellationToken cancellationToken)
    {
        var record = await _catalogContext.SubscriptionRecords.SingleOrDefaultAsync(
            candidate => candidate.SubscriptionReference == subscriptionReference,
            cancellationToken);

        if (record is null)
        {
            record = new SubscriptionRecord(
                userId,
                summary.ProductHandle,
                customerReference,
                subscriptionReference,
                Guid.NewGuid().ToString(),
                summary.ProductName,
                summary.PriceInCents,
                summary.Interval,
                summary.IntervalUnit);
            _catalogContext.SubscriptionRecords.Add(record);
        }

        record.Synchronize(
            summary.ProductHandle,
            summary.ProductName,
            summary.PriceInCents,
            summary.Currency,
            summary.Interval,
            summary.IntervalUnit,
            summary.State,
            summary.NextBillingAt,
            subscription.Customer.Id,
            subscription.Id,
            DateTimeOffset.UtcNow);

        await _catalogContext.SaveChangesAsync(cancellationToken);
    }

    private static SubscriptionPlan ToPlan(MaxioProduct product) => new(
        product.Handle,
        product.Name,
        product.Description ?? string.Empty,
        product.PriceInCents,
        product.Interval,
        product.IntervalUnit);

    private static SubscriptionSummary ToSummary(MaxioSubscription subscription, MaxioProduct product) => new(
        subscription.Id,
        product.Handle,
        product.Name,
        subscription.ProductPriceInCents ?? product.PriceInCents,
        subscription.Currency,
        product.Interval,
        product.IntervalUnit,
        subscription.State,
        subscription.NextAssessmentAt);

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];

    private sealed record MaxioSubscriptionCreation(MaxioSubscription Subscription, bool Created);
}
