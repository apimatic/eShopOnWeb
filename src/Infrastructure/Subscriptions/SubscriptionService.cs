using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.Infrastructure.Subscriptions;

public sealed class SubscriptionService : ISubscriptionService
{
    private readonly CatalogContext _dbContext;
    private readonly ISubscriptionBillingGateway _gateway;
    private readonly SubscriptionOperationLock _operationLock;

    public SubscriptionService(
        CatalogContext dbContext,
        ISubscriptionBillingGateway gateway,
        SubscriptionOperationLock operationLock)
    {
        _dbContext = dbContext;
        _gateway = gateway;
        _operationLock = operationLock;
    }

    public Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken) =>
        _gateway.GetPlansAsync(cancellationToken);

    public async Task<SubscriptionDetails> SubscribeAsync(
        string userId,
        string userName,
        string email,
        string productHandle,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new SubscriptionRequestException("A productHandle is required.");
        }

        var normalizedHandle = productHandle.Trim().ToUpperInvariant();
        var operationKey = string.Concat(userId, ":", normalizedHandle);
        using var operation = await _operationLock.AcquireAsync(operationKey, cancellationToken);

        var plan = await _gateway.GetPlanAsync(productHandle.Trim(), cancellationToken);
        if (plan is null)
        {
            throw new SubscriptionRequestException("The requested subscription plan is not available.", 404);
        }

        var record = await _dbContext.SubscriptionRecords.SingleOrDefaultAsync(
            item => item.UserId == userId && item.NormalizedProductHandle == normalizedHandle,
            cancellationToken);

        var ownsClaim = false;
        if (record is null)
        {
            record = new SubscriptionRecord(
                userId,
                plan.Handle,
                normalizedHandle,
                CreateReference("subscription", userId, normalizedHandle),
                DateTimeOffset.UtcNow);
            _dbContext.SubscriptionRecords.Add(record);

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                ownsClaim = true;
            }
            catch (DbUpdateException)
            {
                _dbContext.Entry(record).State = EntityState.Detached;
                record = await _dbContext.SubscriptionRecords.SingleAsync(
                    item => item.UserId == userId && item.NormalizedProductHandle == normalizedHandle,
                    cancellationToken);
            }
        }

        if (!ownsClaim)
        {
            return await ResolveExistingAsync(record, cancellationToken);
        }

        // The provider may already hold this deterministic reference even when the
        // local ledger was recreated. Reconcile before any new provider write.
        var providerSubscription = await _gateway.FindSubscriptionAsync(
            record.ProviderReference,
            cancellationToken);
        if (providerSubscription is not null)
        {
            record.MarkSucceeded(DateTimeOffset.UtcNow);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return providerSubscription;
        }

        var customer = CreateCustomer(userId, userName, email);
        try
        {
            await _gateway.EnsureCustomerAsync(customer, cancellationToken);
        }
        catch (SubscriptionBillingException)
        {
            // Customer references are provider-unique, so the claimed row remains safe to retry.
            throw;
        }

        record.MarkCreating(DateTimeOffset.UtcNow);
        await _dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            var subscription = await _gateway.CreateSubscriptionAsync(
                plan.Handle,
                customer.Reference,
                record.ProviderReference,
                cancellationToken);
            record.MarkSucceeded(DateTimeOffset.UtcNow);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return subscription;
        }
        catch (SubscriptionBillingException exception) when (exception.OutcomeUnknown)
        {
            record.MarkUnknown(DateTimeOffset.UtcNow);
            await _dbContext.SaveChangesAsync(CancellationToken.None);

            var reconciled = await _gateway.FindSubscriptionAsync(record.ProviderReference, cancellationToken);
            if (reconciled is not null)
            {
                record.MarkSucceeded(DateTimeOffset.UtcNow);
                await _dbContext.SaveChangesAsync(cancellationToken);
                return reconciled;
            }

            throw new SubscriptionBillingException(
                "The subscription outcome is being reconciled. No new subscription will be created.",
                outcomeUnknown: true,
                innerException: exception);
        }
        catch (SubscriptionBillingException exception)
        {
            record.MarkFailed(exception.Message, DateTimeOffset.UtcNow);
            await _dbContext.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<IReadOnlyList<SubscriptionDetails>> GetSubscriptionsAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        var records = await _dbContext.SubscriptionRecords
            .Where(item => item.UserId == userId && item.Status != SubscriptionRecordStatus.Failed)
            .OrderBy(item => item.CreatedAt)
            .ToListAsync(cancellationToken);

        var subscriptions = new List<SubscriptionDetails>(records.Count);
        foreach (var record in records)
        {
            var subscription = await _gateway.FindSubscriptionAsync(record.ProviderReference, cancellationToken);
            if (subscription is null)
            {
                if (record.Status == SubscriptionRecordStatus.Succeeded)
                {
                    record.MarkUnknown(DateTimeOffset.UtcNow);
                }
                continue;
            }

            record.MarkSucceeded(DateTimeOffset.UtcNow);
            subscriptions.Add(subscription);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return subscriptions;
    }

    private async Task<SubscriptionDetails> ResolveExistingAsync(
        SubscriptionRecord record,
        CancellationToken cancellationToken)
    {
        if (record.Status == SubscriptionRecordStatus.Failed)
        {
            throw new SubscriptionRequestException(
                record.FailureMessage ?? "This subscription request was rejected.",
                409);
        }

        var subscription = await _gateway.FindSubscriptionAsync(record.ProviderReference, cancellationToken);
        if (subscription is not null)
        {
            record.MarkSucceeded(DateTimeOffset.UtcNow);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return subscription;
        }

        if (record.Status is SubscriptionRecordStatus.Creating or SubscriptionRecordStatus.Unknown)
        {
            record.MarkUnknown(DateTimeOffset.UtcNow);
            await _dbContext.SaveChangesAsync(cancellationToken);
            throw new SubscriptionRequestException(
                "The existing subscription request is still being reconciled.",
                409);
        }

        throw new SubscriptionRequestException("The existing subscription request is still in progress.", 409);
    }

    private static SubscriptionCustomer CreateCustomer(string userId, string userName, string email)
    {
        var displayName = userName.Split('@', 2)[0];
        var nameParts = displayName.Split(
            new[] { '.', '_', '-', ' ' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var firstName = nameParts.FirstOrDefault() ?? "eShop";
        var lastName = nameParts.Skip(1).FirstOrDefault() ?? "Customer";

        return new SubscriptionCustomer(
            CreateReference("customer", userId),
            CultureInfo.InvariantCulture.TextInfo.ToTitleCase(firstName.ToLowerInvariant()),
            CultureInfo.InvariantCulture.TextInfo.ToTitleCase(lastName.ToLowerInvariant()),
            email);
    }

    private static string CreateReference(string kind, params string[] values)
    {
        var material = string.Join("\n", values);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
        return string.Concat("eshop-", kind, "-", hash.AsSpan(0, 32));
    }
}
