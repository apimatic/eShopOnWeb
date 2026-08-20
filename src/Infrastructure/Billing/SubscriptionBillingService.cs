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
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class SubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly TimeSpan ProvisioningLease = TimeSpan.FromMinutes(2);
    private readonly CatalogContext _dbContext;
    private readonly ISubscriptionBillingGateway _gateway;
    private readonly MaxioOptions _options;
    private readonly SubscriptionOperationLock _operationLock;

    public SubscriptionBillingService(CatalogContext dbContext, ISubscriptionBillingGateway gateway,
        IOptions<MaxioOptions> options, SubscriptionOperationLock operationLock)
    {
        _dbContext = dbContext;
        _gateway = gateway;
        _options = options.Value;
        _operationLock = operationLock;
    }

    public Task<IReadOnlyList<BillingPlan>> GetPlansAsync(CancellationToken cancellationToken = default) =>
        _gateway.GetPlansAsync(cancellationToken);

    public async Task<SubscriptionResult> SubscribeAsync(BillingUser user, string productHandle,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(productHandle)) throw new BillingPlanNotFoundException(productHandle);

        var plans = await _gateway.GetPlansAsync(cancellationToken);
        var plan = plans.SingleOrDefault(x => string.Equals(x.Handle, productHandle, StringComparison.Ordinal));
        if (plan is null) throw new BillingPlanNotFoundException(productHandle);

        var lockKey = $"{user.Id}:{plan.Handle}";
        var operationLock = _operationLock.For(lockKey);
        await operationLock.WaitAsync(cancellationToken);
        try
        {
            var owner = Guid.NewGuid().ToString("D");
            var reference = CreateReference("eshop-sub", lockKey);
            var enrollment = await AcquireEnrollmentAsync(user.Id, plan.Handle, reference, owner,
                cancellationToken);

            try
            {
                var customerReference = CreateReference("eshop-user", user.Id);
                var customer = await EnsureCustomerAsync(user, customerReference, cancellationToken);
                var subscriptions = await _gateway.GetCustomerSubscriptionsAsync(customer.Id, cancellationToken);
                var existing = subscriptions.SingleOrDefault(x =>
                    string.Equals(x.Reference, reference, StringComparison.Ordinal));

                if (existing is not null)
                {
                    await SaveSubscriptionAsync(enrollment, customer.Id, existing, cancellationToken);
                    return new SubscriptionResult(existing, false);
                }

                var created = await _gateway.CreateSubscriptionAsync(customer.Id, plan.Handle, reference,
                    cancellationToken);
                await SaveSubscriptionAsync(enrollment, customer.Id, created, cancellationToken);
                return new SubscriptionResult(created, true);
            }
            catch (MaxioApiException ex) when (!ex.IsTransient)
            {
                enrollment.MarkFailed();
                await TrySaveAsync(cancellationToken);
                throw;
            }
        }
        finally
        {
            operationLock.Release();
        }
    }

    public async Task<IReadOnlyList<BillingSubscription>> GetSubscriptionsAsync(BillingUser user,
        CancellationToken cancellationToken = default)
    {
        var customerReference = CreateReference("eshop-user", user.Id);
        var customer = await _gateway.FindCustomerAsync(customerReference, cancellationToken);
        if (customer is null) return Array.Empty<BillingSubscription>();

        var subscriptions = await _gateway.GetCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Where(x => string.Equals(x.ProductFamilyHandle, _options.ProductFamilyHandle,
                StringComparison.Ordinal))
            .OrderByDescending(x => x.NextBillingAt)
            .ToList();
    }

    private async Task<SubscriptionEnrollment> AcquireEnrollmentAsync(string userId, string productHandle,
        string reference, string owner, CancellationToken cancellationToken)
    {
        var enrollment = await _dbContext.SubscriptionEnrollments.SingleOrDefaultAsync(
            x => x.UserId == userId && x.ProductHandle == productHandle, cancellationToken);
        var now = DateTimeOffset.UtcNow;

        if (enrollment is null)
        {
            enrollment = new SubscriptionEnrollment(userId, productHandle, reference, owner,
                now.Add(ProvisioningLease));
            _dbContext.SubscriptionEnrollments.Add(enrollment);
            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                return enrollment;
            }
            catch (DbUpdateException)
            {
                _dbContext.Entry(enrollment).State = EntityState.Detached;
                enrollment = await _dbContext.SubscriptionEnrollments.SingleAsync(
                    x => x.UserId == userId && x.ProductHandle == productHandle, cancellationToken);
            }
        }

        if (enrollment.ProvisioningState == SubscriptionProvisioningState.Provisioning &&
            enrollment.LeaseExpiresAtUtc > now)
        {
            throw new SubscriptionProvisioningInProgressException();
        }

        enrollment.AcquireLease(owner, now.Add(ProvisioningLease));
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new SubscriptionProvisioningInProgressException();
        }

        return enrollment;
    }

    private async Task<BillingCustomer> EnsureCustomerAsync(BillingUser user, string reference,
        CancellationToken cancellationToken)
    {
        var customer = await _gateway.FindCustomerAsync(reference, cancellationToken);
        if (customer is not null) return customer;

        try
        {
            return await _gateway.CreateCustomerAsync(user, reference, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            customer = await _gateway.FindCustomerAsync(reference, cancellationToken);
            if (customer is not null) return customer;
            throw;
        }
    }

    private async Task SaveSubscriptionAsync(SubscriptionEnrollment enrollment, long customerId,
        BillingSubscription subscription, CancellationToken cancellationToken)
    {
        enrollment.MarkProvisioned(customerId, subscription.Id, subscription.ProductName,
            subscription.PriceInCents, subscription.Interval, subscription.IntervalUnit, subscription.State,
            subscription.NextBillingAt);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task TrySaveAsync(CancellationToken cancellationToken)
    {
        try { await _dbContext.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { }
    }

    private static string CreateReference(string prefix, string source)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return $"{prefix}-{Convert.ToHexString(hash.AsSpan(0, 16)).ToLowerInvariant()}";
    }
}
