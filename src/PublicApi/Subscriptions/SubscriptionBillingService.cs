using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> EnrollmentLocks = new(StringComparer.Ordinal);
    private static readonly TimeSpan PendingAttemptLease = TimeSpan.FromMinutes(2);
    private readonly CatalogContext _dbContext;
    private readonly IMaxioClient _maxioClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<SubscriptionBillingService> _logger;

    public SubscriptionBillingService(
        CatalogContext dbContext,
        IMaxioClient maxioClient,
        IOptions<MaxioOptions> options,
        ILogger<SubscriptionBillingService> logger)
    {
        _dbContext = dbContext;
        _maxioClient = maxioClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _maxioClient.ListProductsAsync(cancellationToken);
        return products
            .Where(product => product.ArchivedAt is null && !string.IsNullOrWhiteSpace(product.Handle))
            .OrderBy(product => product.PriceInCents)
            .Select(product => new SubscriptionPlan(
                product.Id,
                product.Handle!,
                product.Name,
                product.Description,
                product.PriceInCents,
                product.Interval,
                product.IntervalUnit,
                product.RequireCreditCard))
            .ToList();
    }

    public async Task<SubscriptionEnrollment> SubscribeAsync(
        BillingUser user,
        string productHandle,
        CancellationToken cancellationToken = default)
    {
        var plan = (await GetPlansAsync(cancellationToken))
            .SingleOrDefault(candidate => string.Equals(candidate.Handle, productHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new SubscriptionPlanNotFoundException(productHandle);
        }

        var lockKey = $"{user.Id}\n{plan.Handle}";
        var enrollmentLock = EnrollmentLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await enrollmentLock.WaitAsync(cancellationToken);
        try
        {
            return await SubscribeWithinLockAsync(user, plan, cancellationToken);
        }
        finally
        {
            enrollmentLock.Release();
        }
    }

    public async Task<IReadOnlyList<UserSubscription>> GetSubscriptionsAsync(
        BillingUser user,
        CancellationToken cancellationToken = default)
    {
        var customerReference = BuildCustomerReference(user.Id);
        var customer = await _maxioClient.FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<UserSubscription>();
        }

        var subscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var managedSubscriptions = subscriptions
            .Where(subscription => string.Equals(
                subscription.Product?.ProductFamily?.Handle,
                _options.ProductFamilyHandle,
                StringComparison.Ordinal))
            .ToList();

        foreach (var subscription in managedSubscriptions)
        {
            await PersistRecoveredSubscriptionAsync(user.Id, customer.Id, subscription, cancellationToken);
        }

        return managedSubscriptions
            .OrderByDescending(subscription => subscription.Id)
            .Select(MapSubscription)
            .ToList();
    }

    private async Task<SubscriptionEnrollment> SubscribeWithinLockAsync(
        BillingUser user,
        SubscriptionPlan plan,
        CancellationToken cancellationToken)
    {
        var subscriptionReference = BuildSubscriptionReference(user.Id, plan.Handle);
        var (attempt, ownsAttempt) = await AcquireAttemptAsync(
            user.Id,
            plan.Handle,
            subscriptionReference,
            cancellationToken);

        if (!ownsAttempt && attempt.Status == BillingSubscriptionStatus.Pending)
        {
            attempt = await WaitForAttemptAsync(attempt.Id, cancellationToken);
            if (attempt.Status == BillingSubscriptionStatus.Pending)
            {
                throw new SubscriptionEnrollmentInProgressException();
            }
        }

        var existingSubscription = await _maxioClient.FindSubscriptionByReferenceAsync(
            subscriptionReference,
            cancellationToken);
        if (existingSubscription is not null)
        {
            var customerId = existingSubscription.Customer?.Id ?? attempt.MaxioCustomerId;
            if (customerId is null)
            {
                throw new MaxioApiException(System.Net.HttpStatusCode.BadGateway, "Maxio returned a subscription without a customer.");
            }

            attempt.MarkCompleted(customerId.Value, existingSubscription.Id);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return new SubscriptionEnrollment(MapSubscription(existingSubscription), true);
        }

        attempt.MarkPending();
        await _dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            var customerReference = BuildCustomerReference(user.Id);
            var customer = await _maxioClient.FindCustomerByReferenceAsync(customerReference, cancellationToken);
            var customerAttributes = customer is null
                ? new MaxioCreateCustomer
                {
                    Reference = customerReference,
                    Email = user.Email,
                    FirstName = GetFirstName(user.Email),
                    LastName = "Customer"
                }
                : null;
            MaxioSubscription createdSubscription;
            try
            {
                createdSubscription = await _maxioClient.CreateSubscriptionAsync(
                    customer?.Id,
                    customerAttributes,
                    plan.Handle,
                    subscriptionReference,
                    cancellationToken);
            }
            catch (MaxioApiException exception) when ((int)exception.StatusCode == 422)
            {
                // A prior request may have reached Maxio even if its response was lost.
                var reconciledSubscription = await _maxioClient.FindSubscriptionByReferenceAsync(
                    subscriptionReference,
                    cancellationToken);
                if (reconciledSubscription is null)
                {
                    // Two different plan enrollments for a new user can race. Customer
                    // reference uniqueness resolves ownership; retry against the winner.
                    var concurrentCustomer = await _maxioClient.FindCustomerByReferenceAsync(
                        customerReference,
                        cancellationToken);
                    if (customer is not null || concurrentCustomer is null)
                    {
                        throw;
                    }

                    createdSubscription = await _maxioClient.CreateSubscriptionAsync(
                        concurrentCustomer.Id,
                        null,
                        plan.Handle,
                        subscriptionReference,
                        cancellationToken);
                }
                else
                {
                    createdSubscription = reconciledSubscription;
                }
            }

            var createdCustomerId = createdSubscription.Customer?.Id ?? customer?.Id;
            if (createdCustomerId is null)
            {
                throw new MaxioApiException(System.Net.HttpStatusCode.BadGateway, "Maxio returned a subscription without a customer.");
            }

            attempt.MarkCompleted(createdCustomerId.Value, createdSubscription.Id);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return new SubscriptionEnrollment(MapSubscription(createdSubscription), false);
        }
        catch
        {
            attempt.MarkFailed();
            try
            {
                await _dbContext.SaveChangesAsync(CancellationToken.None);
            }
            catch (Exception persistenceException)
            {
                _logger.LogWarning(persistenceException, "Could not mark failed Maxio enrollment attempt {AttemptId}.", attempt.Id);
            }

            throw;
        }
    }

    private async Task<(BillingSubscription Attempt, bool OwnsAttempt)> AcquireAttemptAsync(
        string userId,
        string productHandle,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        var attempt = await _dbContext.BillingSubscriptions
            .SingleOrDefaultAsync(
                subscription => subscription.UserId == userId && subscription.ProductHandle == productHandle,
                cancellationToken);

        if (attempt is null)
        {
            attempt = new BillingSubscription(userId, productHandle, subscriptionReference);
            _dbContext.BillingSubscriptions.Add(attempt);
            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                return (attempt, true);
            }
            catch (DbUpdateException)
            {
                // A unique index arbitrates enrollment ownership between app instances.
                _dbContext.ChangeTracker.Clear();
                attempt = await _dbContext.BillingSubscriptions.SingleAsync(
                    subscription => subscription.UserId == userId && subscription.ProductHandle == productHandle,
                    cancellationToken);
            }
        }

        if (attempt.Status == BillingSubscriptionStatus.Pending &&
            attempt.UpdatedAt >= DateTimeOffset.UtcNow.Subtract(PendingAttemptLease))
        {
            return (attempt, false);
        }

        attempt.MarkPending();
        await _dbContext.SaveChangesAsync(cancellationToken);
        return (attempt, true);
    }

    private async Task<BillingSubscription> WaitForAttemptAsync(int attemptId, CancellationToken cancellationToken)
    {
        for (var index = 0; index < 20; index++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
            _dbContext.ChangeTracker.Clear();
            var attempt = await _dbContext.BillingSubscriptions.SingleAsync(
                subscription => subscription.Id == attemptId,
                cancellationToken);
            if (attempt.Status != BillingSubscriptionStatus.Pending)
            {
                return attempt;
            }
        }

        return await _dbContext.BillingSubscriptions.SingleAsync(
            subscription => subscription.Id == attemptId,
            cancellationToken);
    }

    private async Task PersistRecoveredSubscriptionAsync(
        string userId,
        long customerId,
        MaxioSubscription subscription,
        CancellationToken cancellationToken)
    {
        if (subscription.Product?.Handle is not { Length: > 0 } productHandle)
        {
            return;
        }

        var attempt = await _dbContext.BillingSubscriptions.SingleOrDefaultAsync(
            record => record.UserId == userId && record.ProductHandle == productHandle,
            cancellationToken);
        if (attempt is null)
        {
            attempt = new BillingSubscription(
                userId,
                productHandle,
                subscription.Reference ?? BuildSubscriptionReference(userId, productHandle));
            _dbContext.BillingSubscriptions.Add(attempt);
        }

        attempt.MarkCompleted(customerId, subscription.Id);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            _logger.LogDebug(exception, "A concurrent request already persisted Maxio subscription {SubscriptionId}.", subscription.Id);
            _dbContext.ChangeTracker.Clear();
        }
    }

    private static UserSubscription MapSubscription(MaxioSubscription subscription)
    {
        var product = subscription.Product
            ?? throw new MaxioApiException(System.Net.HttpStatusCode.BadGateway, "Maxio returned a subscription without a product.");
        return new UserSubscription(
            subscription.Id,
            product.Handle ?? string.Empty,
            product.Name,
            subscription.ProductPriceInCents,
            product.Interval,
            product.IntervalUnit,
            subscription.State,
            subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt,
            subscription.Currency);
    }

    internal static string BuildCustomerReference(string userId) => $"eshop-user-{Hash(userId)}";

    internal static string BuildSubscriptionReference(string userId, string productHandle) =>
        $"eshop-sub-{Hash($"{userId}\n{productHandle}")}";

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..32];

    private static string GetFirstName(string email)
    {
        var atIndex = email.IndexOf('@');
        var localPart = atIndex > 0 ? email[..atIndex] : email;
        return string.IsNullOrWhiteSpace(localPart) ? "eShopOnWeb" : localPart;
    }
}
