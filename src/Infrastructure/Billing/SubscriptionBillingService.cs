using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class SubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly SemaphoreSlim[] EnrollmentLocks =
        Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    private readonly IMaxioClient _maxioClient;
    private readonly CatalogContext _catalogContext;
    private readonly MaxioOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SubscriptionBillingService> _logger;

    public SubscriptionBillingService(
        IMaxioClient maxioClient,
        CatalogContext catalogContext,
        IOptions<MaxioOptions> options,
        TimeProvider timeProvider,
        ILogger<SubscriptionBillingService> logger)
    {
        _maxioClient = maxioClient;
        _catalogContext = catalogContext;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default) =>
        _maxioClient.ListPlansAsync(cancellationToken);

    public async Task<SubscriptionSummary> SubscribeAsync(
        BillingUser user,
        string productHandle,
        CancellationToken cancellationToken = default)
    {
        var gate = GetEnrollmentLock(user.Id, productHandle);
        await gate.WaitAsync(cancellationToken);
        IDbContextTransaction? transaction = null;
        try
        {
            if (_catalogContext.Database.IsSqlServer())
            {
                transaction = await _catalogContext.Database.BeginTransactionAsync(cancellationToken);
                await AcquireDistributedEnrollmentLockAsync(
                    CreateReference("eshop-enrollment-lock", $"{user.Id}|{productHandle}"),
                    transaction,
                    cancellationToken);
            }

            var result = await SubscribeCoreAsync(user, productHandle, cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return result;
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }

            gate.Release();
        }
    }

    private async Task<SubscriptionSummary> SubscribeCoreAsync(
        BillingUser user,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var plans = await _maxioClient.ListPlansAsync(cancellationToken);
        var plan = plans.SingleOrDefault(candidate =>
            string.Equals(candidate.Handle, productHandle, StringComparison.Ordinal));
        if (plan is null)
        {
            throw new SubscriptionPlanNotFoundException(productHandle);
        }

        var customerReference = CreateReference("eshop-customer", user.Id);
        var subscriptionReference = CreateReference("eshop-subscription", $"{user.Id}|{plan.Handle}");
        var enrollment = await GetOrCreateEnrollmentAsync(
            user.Id,
            plan.Handle,
            customerReference,
            subscriptionReference,
            cancellationToken);

        var existingSubscription = await _maxioClient.FindSubscriptionAsync(
            subscriptionReference,
            cancellationToken);
        if (existingSubscription is not null)
        {
            await RecordEnrollmentAsync(enrollment, existingSubscription, cancellationToken);
            return ToSummary(existingSubscription);
        }

        _ = await EnsureCustomerAsync(user, customerReference, cancellationToken);
        MaxioSubscription subscription;
        try
        {
            subscription = await _maxioClient.CreateSubscriptionAsync(
                plan.Handle,
                customerReference,
                subscriptionReference,
                cancellationToken);
        }
        catch (BillingProviderException)
        {
            // A timeout or duplicate-reference response can arrive after Maxio committed the signup.
            var recovered = await _maxioClient.FindSubscriptionAsync(subscriptionReference, cancellationToken);
            if (recovered is null)
            {
                throw;
            }

            subscription = recovered;
        }

        await RecordEnrollmentAsync(enrollment, subscription, cancellationToken);
        return ToSummary(subscription);
    }

    private async Task AcquireDistributedEnrollmentLockAsync(
        string resource,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = _catalogContext.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = """
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock
                @Resource = @resource,
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = 30000;
            IF @result < 0
                THROW 51000, 'Could not acquire the subscription enrollment lock.', 1;
            """;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@resource";
        parameter.Value = resource;
        command.Parameters.Add(parameter);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SubscriptionSummary>> ListSubscriptionsAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var customerReference = CreateReference("eshop-customer", userId);
        var customer = await _maxioClient.FindCustomerAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionSummary>();
        }

        var subscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions
            .Where(subscription => string.Equals(
                subscription.ProductFamilyHandle,
                _options.ProductFamilyHandle,
                StringComparison.Ordinal))
            .Select(ToSummary)
            .OrderBy(subscription => subscription.Id)
            .ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(
        BillingUser user,
        string customerReference,
        CancellationToken cancellationToken)
    {
        var customer = await _maxioClient.FindCustomerAsync(customerReference, cancellationToken);
        if (customer is not null)
        {
            return customer;
        }

        var (firstName, lastName) = DeriveNames(user.Email);
        try
        {
            return await _maxioClient.CreateCustomerAsync(
                firstName,
                lastName,
                user.Email,
                customerReference,
                cancellationToken);
        }
        catch (BillingProviderException exception) when (
            exception.IsTransient || exception.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Customer reference is unique in Maxio; lookup reconciles both races and ambiguous timeouts.
            var recovered = await _maxioClient.FindCustomerAsync(customerReference, cancellationToken);
            if (recovered is null)
            {
                throw;
            }

            return recovered;
        }
    }

    private async Task<SubscriptionEnrollment> GetOrCreateEnrollmentAsync(
        string userId,
        string productHandle,
        string customerReference,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        var existing = await _catalogContext.SubscriptionEnrollments.SingleOrDefaultAsync(
            enrollment => enrollment.UserId == userId && enrollment.ProductHandle == productHandle,
            cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var enrollment = new SubscriptionEnrollment(
            userId,
            productHandle,
            customerReference,
            subscriptionReference,
            _timeProvider.GetUtcNow());
        _catalogContext.SubscriptionEnrollments.Add(enrollment);
        try
        {
            await _catalogContext.SaveChangesAsync(cancellationToken);
            return enrollment;
        }
        catch (DbUpdateException)
        {
            _catalogContext.Entry(enrollment).State = EntityState.Detached;
            return await _catalogContext.SubscriptionEnrollments.SingleAsync(
                candidate => candidate.UserId == userId && candidate.ProductHandle == productHandle,
                cancellationToken);
        }
    }

    private async Task RecordEnrollmentAsync(
        SubscriptionEnrollment enrollment,
        MaxioSubscription subscription,
        CancellationToken cancellationToken)
    {
        enrollment.RecordMaxioIds(subscription.CustomerId, subscription.Id, _timeProvider.GetUtcNow());
        try
        {
            await _catalogContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            // Maxio remains authoritative and the deterministic references allow later reconciliation.
            _logger.LogWarning(exception, "Maxio subscription {SubscriptionId} was created but its local mapping was not updated.", subscription.Id);
        }
    }

    private static SubscriptionSummary ToSummary(MaxioSubscription subscription) => new(
        subscription.Id,
        subscription.PlanHandle,
        subscription.PlanName,
        subscription.PricePointName,
        subscription.PriceInCents,
        subscription.Interval,
        subscription.IntervalUnit,
        subscription.State,
        subscription.NextBillingAt);

    private static SemaphoreSlim GetEnrollmentLock(string userId, string productHandle)
    {
        var hash = StringComparer.Ordinal.GetHashCode($"{userId}|{productHandle}") & int.MaxValue;
        return EnrollmentLocks[hash % EnrollmentLocks.Length];
    }

    private static string CreateReference(string prefix, string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return $"{prefix}-{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private static (string FirstName, string LastName) DeriveNames(string email)
    {
        var localPart = email.Split('@', 2)[0];
        var parts = localPart.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);
        var firstName = parts.Length > 0 ? parts[0] : "eShop";
        var lastName = parts.Length > 1 ? string.Join(' ', parts.Skip(1)) : "Customer";
        return (Truncate(firstName, 255), Truncate(lastName, 255));
    }

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];
}
