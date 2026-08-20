using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken);
    Task<SubscribeResult> SubscribeAsync(BillingUser user, string productHandle, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionDto>> ListForUserAsync(BillingUser user, CancellationToken cancellationToken);
}

internal sealed class SubscriptionBillingService : ISubscriptionBillingService
{
    private readonly IMaxioBillingGateway _gateway;
    private readonly CatalogContext _dbContext;
    private readonly SubscriptionKeyLock _keyLock;
    private readonly MaxioOptions _options;

    public SubscriptionBillingService(
        IMaxioBillingGateway gateway,
        CatalogContext dbContext,
        SubscriptionKeyLock keyLock,
        IOptions<MaxioOptions> options)
    {
        _gateway = gateway;
        _dbContext = dbContext;
        _keyLock = keyLock;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _gateway.ListPlansAsync(cancellationToken);
        }
        catch (MaxioDependencyException ex)
        {
            throw DependencyFailure(ex);
        }
    }

    public async Task<SubscribeResult> SubscribeAsync(
        BillingUser user,
        string productHandle,
        CancellationToken cancellationToken)
    {
        productHandle = productHandle.Trim();
        if (productHandle.Length == 0 || productHandle.Length > 100)
        {
            throw new SubscriptionBillingException(HttpStatusCode.BadRequest, "A valid productHandle is required.");
        }

        var lockKey = string.Concat(user.Id, "\n", productHandle);
        using var keyLease = await _keyLock.AcquireAsync(lockKey, cancellationToken);

        try
        {
            var product = await _gateway.FindProductAsync(productHandle, cancellationToken);
            if (product is null || product.IsArchived ||
                !string.Equals(product.ProductFamilyHandle, _options.ProductFamilyHandle, StringComparison.Ordinal))
            {
                throw new SubscriptionBillingException(
                    HttpStatusCode.BadRequest,
                    "The requested subscription plan is not available.");
            }

            var customerReference = BuildReference("eshop-customer-", user.Id);
            var subscriptionReference = BuildReference("eshop-sub-", user.Id + "\n" + productHandle);
            var enrollment = await GetOrCreateEnrollmentAsync(
                user.Id,
                productHandle,
                customerReference,
                subscriptionReference,
                cancellationToken);

            var existing = await _gateway.FindSubscriptionAsync(subscriptionReference, cancellationToken);
            if (existing is not null)
            {
                await ConfirmAsync(enrollment, existing.Id, cancellationToken);
                return new SubscribeResult(existing, Created: false, IsUnknown: false);
            }

            if (enrollment.SendAuthorizedAt.HasValue)
            {
                if (enrollment.Status != SubscriptionEnrollmentStatus.Rejected)
                {
                    enrollment.MarkUnknown(DateTimeOffset.UtcNow);
                    await _dbContext.SaveChangesAsync(cancellationToken);
                }

                return new SubscribeResult(null, Created: false, IsUnknown: true);
            }

            var customer = await EnsureCustomerAsync(user, customerReference, cancellationToken);
            _ = customer;

            var paymentCollectionMethod = await _gateway.ResolveNoCardPaymentCollectionMethodAsync(
                cancellationToken);

            enrollment.AuthorizeSingleSend(DateTimeOffset.UtcNow);
            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                _dbContext.ChangeTracker.Clear();
                var winner = await LoadEnrollmentAsync(user.Id, productHandle, cancellationToken);
                var reconciled = await _gateway.FindSubscriptionAsync(subscriptionReference, cancellationToken);
                if (reconciled is not null)
                {
                    await ConfirmAsync(winner, reconciled.Id, cancellationToken);
                    return new SubscribeResult(reconciled, Created: false, IsUnknown: false);
                }

                winner.MarkUnknown(DateTimeOffset.UtcNow);
                await _dbContext.SaveChangesAsync(cancellationToken);
                return new SubscribeResult(null, Created: false, IsUnknown: true);
            }

            try
            {
                var created = await _gateway.CreateSubscriptionAsync(
                    productHandle,
                    customerReference,
                    subscriptionReference,
                    paymentCollectionMethod,
                    cancellationToken);
                await ConfirmAsync(enrollment, created.Id, cancellationToken);
                return new SubscribeResult(created, Created: true, IsUnknown: false);
            }
            catch (MaxioValidationException ex)
            {
                var reconciled = await ReconcileAfterSendAsync(enrollment, subscriptionReference);
                if (reconciled is not null)
                {
                    return new SubscribeResult(reconciled, Created: true, IsUnknown: false);
                }

                enrollment.MarkRejected(DateTimeOffset.UtcNow);
                await _dbContext.SaveChangesAsync(CancellationToken.None);
                throw new SubscriptionBillingException(
                    HttpStatusCode.UnprocessableEntity,
                    "Maxio rejected the subscription request.",
                    ex);
            }
            catch (Exception ex) when (ex is MaxioUnknownOutcomeException or MaxioDependencyException or OperationCanceledException)
            {
                var reconciled = await ReconcileAfterSendAsync(enrollment, subscriptionReference);
                if (reconciled is not null)
                {
                    return new SubscribeResult(reconciled, Created: true, IsUnknown: false);
                }

                enrollment.MarkUnknown(DateTimeOffset.UtcNow);
                await _dbContext.SaveChangesAsync(CancellationToken.None);
                return new SubscribeResult(null, Created: false, IsUnknown: true);
            }
        }
        catch (MaxioValidationException ex)
        {
            throw new SubscriptionBillingException(HttpStatusCode.UnprocessableEntity, "Maxio rejected the billing request.", ex);
        }
        catch (MaxioDependencyException ex)
        {
            throw DependencyFailure(ex);
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListForUserAsync(
        BillingUser user,
        CancellationToken cancellationToken)
    {
        try
        {
            var customerReference = BuildReference("eshop-customer-", user.Id);
            var customer = await _gateway.FindCustomerAsync(customerReference, cancellationToken);
            return customer is null
                ? Array.Empty<SubscriptionDto>()
                : await _gateway.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        }
        catch (MaxioDependencyException ex)
        {
            throw DependencyFailure(ex);
        }
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(
        BillingUser user,
        string customerReference,
        CancellationToken cancellationToken)
    {
        var existing = await _gateway.FindCustomerAsync(customerReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            return await _gateway.CreateCustomerAsync(user, customerReference, cancellationToken);
        }
        catch (MaxioValidationException)
        {
            var concurrentWinner = await _gateway.FindCustomerAsync(customerReference, cancellationToken);
            if (concurrentWinner is not null)
            {
                return concurrentWinner;
            }

            throw;
        }
    }

    private async Task<SubscriptionEnrollment> GetOrCreateEnrollmentAsync(
        string userId,
        string productHandle,
        string customerReference,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        var existing = await _dbContext.SubscriptionEnrollments
            .SingleOrDefaultAsync(x => x.UserId == userId && x.ProductHandle == productHandle, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var enrollment = new SubscriptionEnrollment(
            userId,
            productHandle,
            customerReference,
            subscriptionReference,
            DateTimeOffset.UtcNow);
        _dbContext.SubscriptionEnrollments.Add(enrollment);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return enrollment;
        }
        catch (DbUpdateException)
        {
            _dbContext.ChangeTracker.Clear();
            return await LoadEnrollmentAsync(userId, productHandle, cancellationToken);
        }
    }

    private Task<SubscriptionEnrollment> LoadEnrollmentAsync(
        string userId,
        string productHandle,
        CancellationToken cancellationToken) =>
        _dbContext.SubscriptionEnrollments.SingleAsync(
            x => x.UserId == userId && x.ProductHandle == productHandle,
            cancellationToken);

    private async Task ConfirmAsync(
        SubscriptionEnrollment enrollment,
        int maxioSubscriptionId,
        CancellationToken cancellationToken)
    {
        enrollment.Confirm(maxioSubscriptionId, DateTimeOffset.UtcNow);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<SubscriptionDto?> ReconcileAfterSendAsync(
        SubscriptionEnrollment enrollment,
        string subscriptionReference)
    {
        using var reconciliationBudget = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            var subscription = await _gateway.FindSubscriptionAsync(
                subscriptionReference,
                reconciliationBudget.Token);
            if (subscription is not null)
            {
                await ConfirmAsync(enrollment, subscription.Id, CancellationToken.None);
            }

            return subscription;
        }
        catch (Exception ex) when (ex is MaxioDependencyException or OperationCanceledException)
        {
            return null;
        }
    }

    private static string BuildReference(string prefix, string value)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return prefix + Convert.ToHexString(digest.AsSpan(0, 20)).ToLowerInvariant();
    }

    private static SubscriptionBillingException DependencyFailure(Exception innerException) =>
        new(HttpStatusCode.BadGateway, "Subscription billing is temporarily unavailable.", innerException);
}
