using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionBillingAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public sealed class SubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ReservationLocks = new();

    private readonly ISubscriptionBillingGateway _gateway;
    private readonly ISubscriptionReservationStore _reservationStore;

    public SubscriptionBillingService(
        ISubscriptionBillingGateway gateway,
        ISubscriptionReservationStore reservationStore)
    {
        _gateway = gateway;
        _reservationStore = reservationStore;
    }

    public Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken) =>
        _gateway.ListPlansAsync(cancellationToken);

    public async Task<BillingSubscription> SubscribeAsync(
        BillingUser user,
        string productHandle,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productHandle);
        var normalizedHandle = productHandle.Trim();
        var lockKey = $"{user.Id}\n{normalizedHandle}";
        var reservationLock = ReservationLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));

        await reservationLock.WaitAsync(cancellationToken);
        try
        {
            return await SubscribeUnderLockAsync(user, normalizedHandle, cancellationToken);
        }
        finally
        {
            reservationLock.Release();
        }
    }

    public async Task<IReadOnlyList<BillingSubscription>> ListMySubscriptionsAsync(
        BillingUser user,
        CancellationToken cancellationToken)
    {
        var customer = await _gateway.FindCustomerAsync(CustomerReference(user.Id), cancellationToken);
        return customer is null
            ? Array.Empty<BillingSubscription>()
            : await _gateway.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    private async Task<BillingSubscription> SubscribeUnderLockAsync(
        BillingUser user,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var plans = await _gateway.ListPlansAsync(cancellationToken);
        if (!plans.Any(plan => string.Equals(plan.Handle, productHandle, StringComparison.Ordinal)))
        {
            throw new ArgumentException("The selected subscription plan is not available.", nameof(productHandle));
        }

        var customerReference = CustomerReference(user.Id);
        var subscriptionReference = SubscriptionReference(user.Id, productHandle);
        var (reservation, _) = await _reservationStore.GetOrCreateAsync(
            user.Id,
            productHandle,
            customerReference,
            subscriptionReference,
            cancellationToken);

        if (reservation.Status == SubscriptionReservationStatus.Completed &&
            reservation.MaxioSubscriptionId is int completedId)
        {
            return await _gateway.ReadSubscriptionAsync(completedId, cancellationToken);
        }

        var customer = await _gateway.EnsureCustomerAsync(user, customerReference, cancellationToken);
        reservation.RecordCustomer(customer.Id);
        await _reservationStore.SaveAsync(cancellationToken);

        var existingSubscription = await _gateway.FindSubscriptionAsync(subscriptionReference, cancellationToken);
        if (existingSubscription is not null)
        {
            reservation.Complete(existingSubscription.Id);
            await _reservationStore.SaveAsync(cancellationToken);
            return existingSubscription;
        }

        if (reservation.Status == SubscriptionReservationStatus.CreateStarted)
        {
            throw new SubscriptionOutcomeUnknownException();
        }

        if (reservation.Status == SubscriptionReservationStatus.Failed)
        {
            reservation.ResetForRetry();
        }

        cancellationToken.ThrowIfCancellationRequested();
        reservation.MarkCreateStarted();
        await _reservationStore.SaveAsync(cancellationToken);

        try
        {
            var created = await _gateway.CreateSubscriptionAsync(
                productHandle,
                customerReference,
                subscriptionReference,
                cancellationToken);
            reservation.Complete(created.Id);
            await _reservationStore.SaveAsync(CancellationToken.None);
            return created;
        }
        catch (BillingProviderException ex) when (!ex.OutcomeMayBeUnknown)
        {
            reservation.MarkFailed();
            await _reservationStore.SaveAsync(CancellationToken.None);
            throw;
        }
        catch (BillingProviderException)
        {
            await _reservationStore.SaveAsync(CancellationToken.None);
            throw new SubscriptionOutcomeUnknownException();
        }
    }

    private static string CustomerReference(string userId) => $"eshop-user-{StableHash(userId)}";

    private static string SubscriptionReference(string userId, string productHandle) =>
        $"eshop-sub-{StableHash($"{userId}\n{productHandle}")}";

    private static string StableHash(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash.AsSpan(0, 16)).ToLowerInvariant();
    }
}
