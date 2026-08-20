using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public sealed class SubscriptionBillingService(
    AppIdentityDbContext identityDbContext,
    ICurrentBillingCustomer currentBillingCustomer,
    IMaxioBillingGateway gateway) : ISubscriptionBillingService
{
    private const int MaximumIdempotencyKeyLength = 128;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new(StringComparer.Ordinal);

    public Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken) =>
        gateway.ListPlansAsync(cancellationToken);

    public async Task<UserSubscription> SubscribeAsync(
        string productHandle,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        productHandle = productHandle?.Trim() ?? string.Empty;
        idempotencyKey = idempotencyKey?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new BillingValidationException("A productHandle is required.");
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > MaximumIdempotencyKeyLength)
        {
            throw new BillingValidationException(
                $"An idempotencyKey between 1 and {MaximumIdempotencyKeyLength} characters is required.");
        }

        var customer = await currentBillingCustomer.GetAsync();
        var lockKey = $"{customer.Subject}\n{productHandle}";
        var subscriptionLock = Locks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await subscriptionLock.WaitAsync(cancellationToken);
        try
        {
            var existingByKey = await identityDbContext.SubscriptionIdempotencyRecords
                .SingleOrDefaultAsync(
                    record => record.UserId == customer.Subject && record.IdempotencyKey == idempotencyKey,
                    cancellationToken);
            if (existingByKey is not null)
            {
                if (!string.Equals(existingByKey.ProductHandle, productHandle, StringComparison.Ordinal))
                {
                    throw new BillingConflictException(
                        "The idempotency key has already been used for a different subscription plan.");
                }

                return await ResolveExistingAsync(existingByKey, cancellationToken);
            }

            var existingByProduct = await identityDbContext.SubscriptionIdempotencyRecords
                .SingleOrDefaultAsync(
                    record => record.UserId == customer.Subject && record.ProductHandle == productHandle,
                    cancellationToken);
            if (existingByProduct is not null)
            {
                return await ResolveExistingAsync(existingByProduct, cancellationToken);
            }

            var now = DateTimeOffset.UtcNow;
            var record = new SubscriptionIdempotencyRecord
            {
                UserId = customer.Subject,
                IdempotencyKey = idempotencyKey,
                ProductHandle = productHandle,
                SubscriptionReference = CreateSubscriptionReference(customer.Subject, idempotencyKey),
                Status = SubscriptionIdempotencyStatus.Pending,
                CreatedAt = now,
                UpdatedAt = now
            };
            identityDbContext.SubscriptionIdempotencyRecords.Add(record);

            try
            {
                await identityDbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                identityDbContext.Entry(record).State = EntityState.Detached;
                var raced = await identityDbContext.SubscriptionIdempotencyRecords
                    .SingleAsync(
                        candidate => candidate.UserId == customer.Subject &&
                                     (candidate.IdempotencyKey == idempotencyKey ||
                                      candidate.ProductHandle == productHandle),
                        cancellationToken);
                return await ResolveExistingAsync(raced, cancellationToken);
            }

            try
            {
                var recovered = await gateway.FindSubscriptionAsync(record.SubscriptionReference, cancellationToken);
                var subscription = recovered ?? await gateway.CreateSubscriptionAsync(
                    customer,
                    productHandle,
                    record.SubscriptionReference,
                    cancellationToken);
                await CompleteAsync(record, subscription, cancellationToken);
                return subscription;
            }
            catch (BillingException ex) when ((int)ex.StatusCode is >= 400 and < 500)
            {
                record.Status = SubscriptionIdempotencyStatus.Failed;
                record.UpdatedAt = DateTimeOffset.UtcNow;
                await identityDbContext.SaveChangesAsync(cancellationToken);
                throw;
            }
        }
        finally
        {
            subscriptionLock.Release();
        }
    }

    public async Task<IReadOnlyList<UserSubscription>> ListMySubscriptionsAsync(
        CancellationToken cancellationToken)
    {
        var customer = await currentBillingCustomer.GetAsync();
        return await gateway.ListSubscriptionsAsync(customer.MaxioReference, cancellationToken);
    }

    private async Task<UserSubscription> ResolveExistingAsync(
        SubscriptionIdempotencyRecord record,
        CancellationToken cancellationToken)
    {
        if (record.Status == SubscriptionIdempotencyStatus.Completed && record.ResponseJson is not null)
        {
            return JsonSerializer.Deserialize<UserSubscription>(record.ResponseJson) ??
                   throw new MaxioProviderException(
                       System.Net.HttpStatusCode.InternalServerError,
                       "The stored subscription response could not be read.");
        }

        if (record.Status == SubscriptionIdempotencyStatus.Failed)
        {
            throw new BillingConflictException(
                "This subscription request previously failed. Use a new idempotency key after correcting the request.");
        }

        var reconciled = await gateway.FindSubscriptionAsync(record.SubscriptionReference, cancellationToken);
        if (reconciled is null)
        {
            throw new BillingConflictException("This subscription request is already in progress.");
        }

        await CompleteAsync(record, reconciled, cancellationToken);
        return reconciled;
    }

    private async Task CompleteAsync(
        SubscriptionIdempotencyRecord record,
        UserSubscription subscription,
        CancellationToken cancellationToken)
    {
        record.Status = SubscriptionIdempotencyStatus.Completed;
        record.ResponseJson = JsonSerializer.Serialize(subscription);
        record.UpdatedAt = DateTimeOffset.UtcNow;
        await identityDbContext.SaveChangesAsync(cancellationToken);
    }

    private static string CreateSubscriptionReference(string subject, string idempotencyKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{subject}\n{idempotencyKey}"));
        return $"eshop-sub:{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }
}
