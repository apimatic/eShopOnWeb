using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.Infrastructure.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed record SubscriptionShopper(string UserId, string Email);

public sealed record SubscribeResult(SubscriptionDto Subscription, bool Created);

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionDto>> GetSubscriptionsAsync(SubscriptionShopper shopper,
        CancellationToken cancellationToken);
    Task<SubscribeResult> SubscribeAsync(SubscriptionShopper shopper, string productHandle,
        CancellationToken cancellationToken);
}

public sealed class SubscriptionCreationCoordinator
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public SemaphoreSlim For(string userId, string productHandle) =>
        _locks.GetOrAdd($"{userId}\n{productHandle}", _ => new SemaphoreSlim(1, 1));
}

public sealed class SubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly TimeSpan CreationLease = TimeSpan.FromSeconds(45);
    private readonly IMaxioClient _maxio;
    private readonly SubscriptionDbContext _subscriptionContext;
    private readonly SubscriptionCreationCoordinator _coordinator;

    public SubscriptionBillingService(IMaxioClient maxio, SubscriptionDbContext subscriptionContext,
        SubscriptionCreationCoordinator coordinator)
    {
        _maxio = maxio;
        _subscriptionContext = subscriptionContext;
        _coordinator = coordinator;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
    {
        var products = await _maxio.ListProductsAsync(cancellationToken);
        return products
            .Where(product => product.ArchivedAt is null && !string.IsNullOrWhiteSpace(product.Handle))
            .OrderBy(product => product.PriceInCents)
            .ThenBy(product => product.Name, StringComparer.OrdinalIgnoreCase)
            .Select(ToPlanDto)
            .ToArray();
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetSubscriptionsAsync(SubscriptionShopper shopper,
        CancellationToken cancellationToken)
    {
        var customer = await _maxio.FindCustomerAsync(CustomerReference(shopper.UserId), cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions
            .OrderBy(subscription => subscription.Product.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(subscription => subscription.Id)
            .Select(ToSubscriptionDto)
            .ToArray();
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscriptionShopper shopper, string productHandle,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new SubscriptionRequestException("productHandle is required.");
        }

        productHandle = productHandle.Trim();
        var products = await _maxio.ListProductsAsync(cancellationToken);
        var product = products.SingleOrDefault(item =>
            item.ArchivedAt is null && string.Equals(item.Handle, productHandle, StringComparison.Ordinal));
        if (product is null)
        {
            throw new SubscriptionRequestException(
                $"Product '{productHandle}' is not an available plan in the configured Maxio product family.");
        }

        var gate = _coordinator.For(shopper.UserId, productHandle);
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await SubscribeUnderLockAsync(shopper, product, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<SubscribeResult> SubscribeUnderLockAsync(SubscriptionShopper shopper,
        MaxioProduct product, CancellationToken cancellationToken)
    {
        var customer = await EnsureCustomerAsync(shopper, cancellationToken);
        var reference = SubscriptionReference(shopper.UserId, product.Handle!);
        var existing = await _maxio.FindSubscriptionAsync(reference, cancellationToken);
        if (existing is not null)
        {
            ValidateOwnership(existing, customer.Id, product.Handle!, reference);
            await CompleteLocalRecordAsync(shopper.UserId, product.Handle!, reference, customer.Id, existing.Id,
                cancellationToken);
            return new SubscribeResult(ToSubscriptionDto(existing), false);
        }

        var (record, ownsCreation) = await ReserveCreationAsync(shopper.UserId, product.Handle!, reference,
            cancellationToken);
        if (!ownsCreation)
        {
            var completed = await WaitForConcurrentCreationAsync(record, customer.Id, product.Handle!, reference,
                cancellationToken);
            if (completed is not null)
            {
                return new SubscribeResult(ToSubscriptionDto(completed), false);
            }

            record = await ClaimExpiredLeaseAsync(record.Id, cancellationToken);
        }

        MaxioSubscription? created;
        try
        {
            created = await _maxio.CreateSubscriptionAsync(customer.Id, product.Handle!, reference,
                cancellationToken);
        }
        catch (MaxioApiException exception) when (exception.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // If another completed request won at Maxio, its deterministic reference is recoverable.
            created = await _maxio.FindSubscriptionAsync(reference, cancellationToken);
            if (created is null)
            {
                _subscriptionContext.SubscriptionRecords.Remove(record);
                await _subscriptionContext.SaveChangesAsync(cancellationToken);
                throw;
            }
        }

        if (created is null)
        {
            throw new MaxioApiException(HttpStatusCode.BadGateway,
                "Maxio returned an empty subscription response.");
        }

        ValidateOwnership(created, customer.Id, product.Handle!, reference);
        record.Complete(customer.Id, created.Id);
        await _subscriptionContext.SaveChangesAsync(cancellationToken);
        return new SubscribeResult(ToSubscriptionDto(created), true);
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(SubscriptionShopper shopper,
        CancellationToken cancellationToken)
    {
        var reference = CustomerReference(shopper.UserId);
        var customer = await _maxio.FindCustomerAsync(reference, cancellationToken);
        if (customer is not null)
        {
            return customer;
        }

        try
        {
            return await _maxio.CreateCustomerAsync(shopper.Email, reference, cancellationToken);
        }
        catch (MaxioApiException exception) when (exception.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Customer references are unique according to the Maxio OpenAPI operation description.
            var concurrentCustomer = await _maxio.FindCustomerAsync(reference, cancellationToken);
            if (concurrentCustomer is null)
            {
                throw;
            }

            return concurrentCustomer;
        }
    }

    private async Task<(SubscriptionRecord Record, bool OwnsCreation)> ReserveCreationAsync(string userId,
        string productHandle, string reference, CancellationToken cancellationToken)
    {
        var existing = await _subscriptionContext.SubscriptionRecords.SingleOrDefaultAsync(record =>
            record.UserId == userId && record.ProductHandle == productHandle, cancellationToken);
        if (existing is not null)
        {
            return (existing, false);
        }

        var token = Guid.NewGuid().ToString();
        var record = new SubscriptionRecord(userId, productHandle, reference, token,
            DateTimeOffset.UtcNow.Add(CreationLease));
        _subscriptionContext.SubscriptionRecords.Add(record);
        try
        {
            await _subscriptionContext.SaveChangesAsync(cancellationToken);
            return (record, true);
        }
        catch (DbUpdateException)
        {
            _subscriptionContext.ChangeTracker.Clear();
            existing = await _subscriptionContext.SubscriptionRecords.SingleAsync(item =>
                item.UserId == userId && item.ProductHandle == productHandle, cancellationToken);
            return (existing, false);
        }
    }

    private async Task<MaxioSubscription?> WaitForConcurrentCreationAsync(SubscriptionRecord record,
        int customerId, string productHandle, string reference, CancellationToken cancellationToken)
    {
        while (DateTimeOffset.UtcNow < record.CreationLeaseExpiresAt)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
            _subscriptionContext.Entry(record).State = EntityState.Detached;
            record = await _subscriptionContext.SubscriptionRecords.AsNoTracking()
                .SingleAsync(item => item.Id == record.Id, cancellationToken);
            if (record.MaxioSubscriptionId is not null)
            {
                var subscription = await _maxio.FindSubscriptionAsync(reference, cancellationToken);
                if (subscription is not null)
                {
                    ValidateOwnership(subscription, customerId, productHandle, reference);
                    return subscription;
                }
            }
        }

        var recovered = await _maxio.FindSubscriptionAsync(reference, cancellationToken);
        if (recovered is not null)
        {
            ValidateOwnership(recovered, customerId, productHandle, reference);
            await CompleteLocalRecordAsync(record.UserId, productHandle, reference, customerId, recovered.Id,
                cancellationToken);
        }

        return recovered;
    }

    private async Task<SubscriptionRecord> ClaimExpiredLeaseAsync(int recordId,
        CancellationToken cancellationToken)
    {
        var token = Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow;
        if (_subscriptionContext.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory")
        {
            _subscriptionContext.ChangeTracker.Clear();
            var inMemoryRecord = await _subscriptionContext.SubscriptionRecords.SingleAsync(
                record => record.Id == recordId, cancellationToken);
            if (inMemoryRecord.MaxioSubscriptionId is not null || inMemoryRecord.CreationLeaseExpiresAt > now)
            {
                throw new SubscriptionConflictException("Another request is already creating this subscription.");
            }

            inMemoryRecord.RenewLease(token, now.Add(CreationLease));
            await _subscriptionContext.SaveChangesAsync(cancellationToken);
            return inMemoryRecord;
        }

        var updated = await _subscriptionContext.SubscriptionRecords
            .Where(record => record.Id == recordId && record.MaxioSubscriptionId == null &&
                             record.CreationLeaseExpiresAt <= now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(record => record.CreationToken, token)
                .SetProperty(record => record.CreationLeaseExpiresAt, now.Add(CreationLease))
                .SetProperty(record => record.UpdatedAt, now), cancellationToken);
        if (updated != 1)
        {
            throw new SubscriptionConflictException("Another request is already creating this subscription.");
        }

        _subscriptionContext.ChangeTracker.Clear();
        return await _subscriptionContext.SubscriptionRecords.SingleAsync(record => record.Id == recordId,
            cancellationToken);
    }

    private async Task CompleteLocalRecordAsync(string userId, string productHandle, string reference,
        int customerId, int subscriptionId, CancellationToken cancellationToken)
    {
        var record = await _subscriptionContext.SubscriptionRecords.SingleOrDefaultAsync(item =>
            item.UserId == userId && item.ProductHandle == productHandle, cancellationToken);
        if (record is null)
        {
            record = new SubscriptionRecord(userId, productHandle, reference, Guid.NewGuid().ToString(),
                DateTimeOffset.MinValue);
            _subscriptionContext.SubscriptionRecords.Add(record);
        }

        record.Complete(customerId, subscriptionId);
        await _subscriptionContext.SaveChangesAsync(cancellationToken);
    }

    private static void ValidateOwnership(MaxioSubscription subscription, int customerId, string productHandle,
        string reference)
    {
        if (subscription.Customer.Id != customerId ||
            !string.Equals(subscription.Product.Handle, productHandle, StringComparison.Ordinal) ||
            !string.Equals(subscription.Reference, reference, StringComparison.Ordinal))
        {
            throw new MaxioApiException(HttpStatusCode.BadGateway,
                "Maxio returned a subscription that does not match the requested customer, plan, and reference.");
        }
    }

    private static SubscriptionPlanDto ToPlanDto(MaxioProduct product) =>
        new(product.Id, product.Handle!, product.Name, product.Description, product.PriceInCents,
            product.Interval, product.IntervalUnit, product.RequireCreditCard);

    private static SubscriptionDto ToSubscriptionDto(MaxioSubscription subscription) =>
        new(subscription.Id, subscription.Product.Handle ?? string.Empty, subscription.Product.Name,
            subscription.ProductPriceInCents, subscription.Product.Interval, subscription.Product.IntervalUnit,
            subscription.State, subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
            subscription.Currency);

    internal static string CustomerReference(string userId) => $"eshop-user:{userId}";

    internal static string SubscriptionReference(string userId, string productHandle) =>
        $"eshop-subscription:{userId}:{productHandle}";
}

public sealed class SubscriptionRequestException : Exception
{
    public SubscriptionRequestException(string message) : base(message) { }
}

public sealed class SubscriptionConflictException : Exception
{
    public SubscriptionConflictException(string message) : base(message) { }
}
