using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public sealed class SubscriptionBillingService : ISubscriptionBillingService
{
    private readonly CatalogContext _dbContext;
    private readonly IMaxioBillingGateway _gateway;
    private readonly AsyncKeyedLocker _locker;

    public SubscriptionBillingService(CatalogContext dbContext, IMaxioBillingGateway gateway, AsyncKeyedLocker locker)
    {
        _dbContext = dbContext;
        _gateway = gateway;
        _locker = locker;
    }

    public Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken) =>
        _gateway.ListPlansAsync(cancellationToken);

    public async Task<SubscriptionDetails> SubscribeAsync(
        BillingUser user,
        string productHandle,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new SubscriptionBillingException(HttpStatusCode.BadRequest, "A productHandle is required.");
        }

        productHandle = productHandle.Trim();
        var lockKey = $"{user.Id}\n{productHandle}";
        using var lease = await _locker.LockAsync(lockKey, cancellationToken);

        var plans = await _gateway.ListPlansAsync(cancellationToken);
        if (!plans.Any(x => string.Equals(x.Handle, productHandle, StringComparison.Ordinal)))
        {
            throw new SubscriptionBillingException(HttpStatusCode.BadRequest, "The requested subscription plan is not available.");
        }

        var enrollment = await _dbContext.SubscriptionEnrollments.SingleOrDefaultAsync(
            x => x.UserId == user.Id && x.ProductHandle == productHandle,
            cancellationToken);

        if (enrollment is not null)
        {
            return await ReconcileExistingAsync(enrollment, cancellationToken);
        }

        var customerReference = StableReference("eshop-customer", user.Id);
        var subscriptionReference = StableReference("eshop-subscription", $"{user.Id}\n{productHandle}");
        enrollment = new SubscriptionEnrollment(user.Id, productHandle, customerReference, subscriptionReference);
        _dbContext.SubscriptionEnrollments.Add(enrollment);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _dbContext.Entry(enrollment).State = EntityState.Detached;
            var concurrent = await _dbContext.SubscriptionEnrollments.SingleAsync(
                x => x.UserId == user.Id && x.ProductHandle == productHandle,
                cancellationToken);
            return await ReconcileExistingAsync(concurrent, cancellationToken);
        }

        try
        {
            var customer = await _gateway.FindCustomerAsync(customerReference, cancellationToken)
                ?? await _gateway.CreateCustomerAsync(user, customerReference, cancellationToken);
            var existing = await _gateway.FindSubscriptionAsync(subscriptionReference, cancellationToken);
            var subscription = existing ?? await _gateway.CreateSubscriptionAsync(
                productHandle,
                customer.Reference,
                subscriptionReference,
                cancellationToken);

            enrollment.Confirm(subscription.Id);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return subscription;
        }
        catch (SubscriptionBillingException ex) when (ex.StatusCode is HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout)
        {
            SubscriptionDetails? reconciled = null;
            try
            {
                reconciled = await _gateway.FindSubscriptionAsync(subscriptionReference, cancellationToken);
            }
            catch (SubscriptionBillingException)
            {
                // The durable unresolved state below prevents a blind second create.
            }
            if (reconciled is not null)
            {
                enrollment.Confirm(reconciled.Id);
                await _dbContext.SaveChangesAsync(cancellationToken);
                return reconciled;
            }

            enrollment.MarkUnresolved();
            await _dbContext.SaveChangesAsync(cancellationToken);
            throw new SubscriptionBillingException(
                HttpStatusCode.Conflict,
                "The subscription outcome is still being reconciled. Retry this same request later; no new enrollment will be sent.",
                ex);
        }
        catch (SubscriptionBillingException ex) when (ex.StatusCode is >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError)
        {
            // A deterministic provider rejection did not create a subscription. Release the
            // local claim so a corrected request or configuration can retry safely.
            _dbContext.SubscriptionEnrollments.Remove(enrollment);
            await _dbContext.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    public async Task<IReadOnlyList<SubscriptionDetails>> ListSubscriptionsAsync(
        BillingUser user,
        CancellationToken cancellationToken)
    {
        var customerReference = StableReference("eshop-customer", user.Id);
        var customer = await _gateway.FindCustomerAsync(customerReference, cancellationToken);
        return customer is null
            ? Array.Empty<SubscriptionDetails>()
            : await _gateway.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    private async Task<SubscriptionDetails> ReconcileExistingAsync(
        SubscriptionEnrollment enrollment,
        CancellationToken cancellationToken)
    {
        var subscription = await _gateway.FindSubscriptionAsync(enrollment.SubscriptionReference, cancellationToken);
        if (subscription is null)
        {
            throw new SubscriptionBillingException(
                HttpStatusCode.Conflict,
                "This enrollment is already in progress or awaiting reconciliation. No duplicate subscription was created.");
        }

        if (enrollment.Status != SubscriptionEnrollmentStatus.Confirmed ||
            enrollment.MaxioSubscriptionId != subscription.Id)
        {
            enrollment.Confirm(subscription.Id);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return subscription;
    }

    private static string StableReference(string prefix, string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return $"{prefix}-{Convert.ToHexString(hash).ToLowerInvariant()}";
    }
}
