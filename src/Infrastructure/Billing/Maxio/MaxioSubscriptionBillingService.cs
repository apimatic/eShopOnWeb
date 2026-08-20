using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private readonly IMaxioClient _maxio;
    private readonly CatalogContext _context;
    private readonly SubscriptionOperationLock _operationLock;
    private readonly MaxioOptions _options;

    public MaxioSubscriptionBillingService(
        IMaxioClient maxio,
        CatalogContext context,
        SubscriptionOperationLock operationLock,
        IOptions<MaxioOptions> options)
    {
        _maxio = maxio;
        _context = context;
        _operationLock = operationLock;
        _options = options.Value;
    }

    public Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        return _maxio.ListPlansAsync(cancellationToken);
    }

    public async Task<SubscriptionDetails> SubscribeAsync(
        SubscriptionUser user,
        string productHandle,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(productHandle) || productHandle.Length > 100)
        {
            throw new ArgumentException("A valid productHandle is required.", nameof(productHandle));
        }

        var plans = await _maxio.ListPlansAsync(cancellationToken);
        var plan = plans.SingleOrDefault(candidate =>
            string.Equals(candidate.Handle, productHandle.Trim(), StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new SubscriptionPlanNotFoundException(productHandle);
        }

        using var localLock = await _operationLock.AcquireAsync(user.UserId, cancellationToken);

        if (_context.Database.IsSqlServer())
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
            await AcquireSqlServerLockAsync(user.UserId, cancellationToken);
            var result = await SubscribeCoreAsync(user, plan.Handle, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }

        return await SubscribeCoreAsync(user, plan.Handle, cancellationToken);
    }

    public async Task<IReadOnlyList<SubscriptionDetails>> GetSubscriptionsAsync(
        SubscriptionUser user,
        CancellationToken cancellationToken = default)
    {
        var customer = await _maxio.FindCustomerAsync(CreateCustomerReference(user.UserId), cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDetails>();
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions
            .Where(subscription => string.Equals(
                subscription.ProductFamilyHandle,
                _options.ProductFamilyHandle,
                StringComparison.Ordinal))
            .Select(subscription => subscription.Details)
            .OrderByDescending(subscription => subscription.NextBillingAt)
            .ThenBy(subscription => subscription.PlanName, StringComparer.Ordinal)
            .ToArray();
    }

    private async Task<SubscriptionDetails> SubscribeCoreAsync(
        SubscriptionUser user,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var customerReference = CreateCustomerReference(user.UserId);
        var subscriptionReference = CreateSubscriptionReference(user.UserId, productHandle);

        var existingSubscription = await _maxio.FindSubscriptionAsync(subscriptionReference, cancellationToken);
        if (existingSubscription is not null)
        {
            EnsureExpectedSubscription(existingSubscription, customerReference, productHandle);
            await SaveEnrollmentAsync(user.UserId, productHandle, existingSubscription, cancellationToken);
            return existingSubscription.Details;
        }

        var customer = await _maxio.FindCustomerAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            customer = await CreateOrRecoverCustomerAsync(user, customerReference, cancellationToken);
        }

        // Reconcile subscriptions created by an earlier integration version or a request
        // that reached Maxio before this process received/persisted its response.
        var existingForPlan = (await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken))
            .FirstOrDefault(candidate => string.Equals(
                candidate.Details.PlanHandle,
                productHandle,
                StringComparison.Ordinal));
        if (existingForPlan is not null)
        {
            EnsureExpectedSubscription(existingForPlan, customerReference, productHandle);
            await SaveEnrollmentAsync(user.UserId, productHandle, existingForPlan, cancellationToken);
            return existingForPlan.Details;
        }

        var subscription = await _maxio.CreateSubscriptionAsync(
            customer.Reference,
            productHandle,
            subscriptionReference,
            cancellationToken);
        EnsureExpectedSubscription(subscription, customerReference, productHandle);
        await SaveEnrollmentAsync(user.UserId, productHandle, subscription, cancellationToken);
        return subscription.Details;
    }

    private async Task<MaxioCustomer> CreateOrRecoverCustomerAsync(
        SubscriptionUser user,
        string customerReference,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _maxio.CreateCustomerAsync(user, customerReference, cancellationToken);
        }
        catch (MaxioApiException exception) when (exception.StatusCode == 422)
        {
            // Customer references are unique in Maxio. A competing or previously timed-out
            // request may have created the customer, so reconcile before surfacing the error.
            var existing = await _maxio.FindCustomerAsync(customerReference, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            throw;
        }
    }

    private async Task SaveEnrollmentAsync(
        string userId,
        string productHandle,
        MaxioSubscription subscription,
        CancellationToken cancellationToken)
    {
        var enrollment = await _context.SubscriptionEnrollments.SingleOrDefaultAsync(
            candidate => candidate.UserId == userId && candidate.ProductHandle == productHandle,
            cancellationToken);

        if (enrollment is null)
        {
            enrollment = new SubscriptionEnrollment(
                userId,
                productHandle,
                subscription.CustomerId,
                subscription.Details.Id);
            await _context.SubscriptionEnrollments.AddAsync(enrollment, cancellationToken);
        }
        else
        {
            enrollment.UpdateMaxioIds(subscription.CustomerId, subscription.Details.Id);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    private static void EnsureExpectedSubscription(
        MaxioSubscription subscription,
        string customerReference,
        string productHandle)
    {
        if (!string.Equals(subscription.CustomerReference, customerReference, StringComparison.Ordinal) ||
            !string.Equals(subscription.Details.PlanHandle, productHandle, StringComparison.Ordinal))
        {
            throw new MaxioApiException(502, new[] { "Maxio returned a subscription that did not match the requested customer and plan." });
        }
    }

    private async Task AcquireSqlServerLockAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        var resource = $"eshop-maxio-{CreateStableToken(userId)}";
        await _context.Database.ExecuteSqlInterpolatedAsync($@"
DECLARE @result int;
EXEC @result = sp_getapplock
    @Resource = {resource},
    @LockMode = 'Exclusive',
    @LockOwner = 'Transaction',
    @LockTimeout = 30000;
IF @result < 0 THROW 51000, 'Could not acquire the subscription enrollment lock.', 1;",
            cancellationToken);
    }

    private static string CreateCustomerReference(string userId)
    {
        return $"eshop-user-{CreateStableToken(userId)}";
    }

    private static string CreateSubscriptionReference(string userId, string productHandle)
    {
        return $"eshop-sub-{CreateStableToken($"{userId}\n{productHandle}")}";
    }

    private static string CreateStableToken(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
