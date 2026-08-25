using System;
using System.Collections.Concurrent;
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
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal sealed class SubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly TimeSpan OperationLease = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan ReconciliationDelay = TimeSpan.FromMilliseconds(250);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new(StringComparer.Ordinal);

    private readonly CatalogContext _db;
    private readonly IMaxioBillingGateway _maxio;
    private readonly ILogger<SubscriptionBillingService> _logger;

    public SubscriptionBillingService(
        CatalogContext db,
        IMaxioBillingGateway maxio,
        ILogger<SubscriptionBillingService> logger)
    {
        _db = db;
        _maxio = maxio;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _maxio.ListPlansAsync(cancellationToken);
        }
        catch (MaxioProviderException ex)
        {
            throw Translate(ex);
        }
    }

    public async Task<SubscriptionDto> SubscribeAsync(
        BillingUser user,
        string productHandle,
        CancellationToken cancellationToken)
    {
        productHandle = productHandle?.Trim() ?? string.Empty;
        if (productHandle.Length is 0 or > 100)
        {
            throw new SubscriptionBillingException(HttpStatusCode.BadRequest, "A valid product handle is required.");
        }

        IReadOnlyList<SubscriptionPlanDto> plans;
        try
        {
            plans = await _maxio.ListPlansAsync(cancellationToken);
        }
        catch (MaxioProviderException ex)
        {
            throw Translate(ex);
        }

        if (!plans.Any(plan => string.Equals(plan.Handle, productHandle, StringComparison.Ordinal)))
        {
            throw new SubscriptionBillingException(
                HttpStatusCode.BadRequest,
                "The requested subscription plan is not available in the configured product family.");
        }

        var gate = Locks.GetOrAdd($"subscription:{user.Id}:{productHandle}", _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await SubscribeUnderLockAsync(user, productHandle, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(
        BillingUser user,
        CancellationToken cancellationToken)
    {
        var customerReference = StableReference("cust", user.Id);
        try
        {
            var customer = await _maxio.FindCustomerAsync(customerReference, cancellationToken);
            if (customer is null) return Array.Empty<SubscriptionDto>();
            return await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        }
        catch (MaxioProviderException ex)
        {
            throw Translate(ex);
        }
    }

    private async Task<SubscriptionDto> SubscribeUnderLockAsync(
        BillingUser user,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var ownerToken = Guid.NewGuid().ToString("N");
        var subscriptionReference = StableReference("sub", user.Id, productHandle);
        var enrollment = await TryClaimEnrollmentAsync(
            user.Id,
            productHandle,
            subscriptionReference,
            ownerToken,
            cancellationToken);

        if (enrollment is null)
        {
            return await WaitForSubscriptionAsync(subscriptionReference, cancellationToken);
        }

        try
        {
            var existing = await _maxio.FindSubscriptionAsync(subscriptionReference, cancellationToken);
            if (existing is not null)
            {
                enrollment.Complete(existing.MaxioSubscriptionId, DateTimeOffset.UtcNow);
                await SaveCompletionAsync(cancellationToken);
                return existing;
            }

            var customer = await EnsureCustomerAsync(user, cancellationToken);
            var created = await _maxio.CreateSubscriptionAsync(
                customer.Reference,
                productHandle,
                subscriptionReference,
                cancellationToken);

            enrollment.Complete(created.MaxioSubscriptionId, DateTimeOffset.UtcNow);
            await SaveCompletionAsync(cancellationToken);
            return created;
        }
        catch (MaxioProviderException ex)
        {
            if (ex.Kind is MaxioFailureKind.AmbiguousWrite or MaxioFailureKind.Transport)
            {
                enrollment.MarkUncertain(DateTimeOffset.UtcNow, OperationLease);
                await TrySaveUncertainAsync(cancellationToken);
            }

            throw Translate(ex);
        }
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(BillingUser user, CancellationToken cancellationToken)
    {
        var gate = Locks.GetOrAdd($"customer:{user.Id}", _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var reference = StableReference("cust", user.Id);
            var ownerToken = Guid.NewGuid().ToString("N");
            var link = await TryClaimCustomerAsync(user.Id, reference, ownerToken, cancellationToken);

            if (link is null)
            {
                return await WaitForCustomerAsync(reference, cancellationToken);
            }

            try
            {
                var existing = await _maxio.FindCustomerAsync(reference, cancellationToken);
                var customer = existing ?? await _maxio.CreateCustomerAsync(user, reference, cancellationToken);
                link.Complete(customer.Id, DateTimeOffset.UtcNow);
                await SaveCompletionAsync(cancellationToken);
                return customer;
            }
            catch (MaxioProviderException ex)
            {
                if (ex.Kind is MaxioFailureKind.AmbiguousWrite or MaxioFailureKind.Transport)
                {
                    link.MarkUncertain(DateTimeOffset.UtcNow, OperationLease);
                    await TrySaveUncertainAsync(cancellationToken);
                }

                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<MaxioSubscriptionEnrollment?> TryClaimEnrollmentAsync(
        string userId,
        string productHandle,
        string reference,
        string ownerToken,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var now = DateTimeOffset.UtcNow;
            var enrollment = await _db.MaxioSubscriptionEnrollments
                .SingleOrDefaultAsync(x => x.UserId == userId && x.ProductHandle == productHandle, cancellationToken);

            if (enrollment is null)
            {
                enrollment = new MaxioSubscriptionEnrollment(
                    userId,
                    productHandle,
                    reference,
                    ownerToken,
                    now,
                    OperationLease);
                _db.MaxioSubscriptionEnrollments.Add(enrollment);
            }
            else if (!enrollment.TryAcquire(ownerToken, now, OperationLease))
            {
                return null;
            }

            try
            {
                await _db.SaveChangesAsync(cancellationToken);
                return enrollment;
            }
            catch (DbUpdateException)
            {
                DetachChangedEntries();
            }
        }

        throw new SubscriptionBillingException(
            HttpStatusCode.Conflict,
            "A subscription enrollment is already in progress. Retry shortly.");
    }

    private async Task<MaxioCustomerLink?> TryClaimCustomerAsync(
        string userId,
        string reference,
        string ownerToken,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var now = DateTimeOffset.UtcNow;
            var link = await _db.MaxioCustomerLinks.SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);
            if (link is null)
            {
                link = new MaxioCustomerLink(userId, reference, ownerToken, now, OperationLease);
                _db.MaxioCustomerLinks.Add(link);
            }
            else if (!link.TryAcquire(ownerToken, now, OperationLease))
            {
                return null;
            }

            try
            {
                await _db.SaveChangesAsync(cancellationToken);
                return link;
            }
            catch (DbUpdateException)
            {
                DetachChangedEntries();
            }
        }

        throw new SubscriptionBillingException(
            HttpStatusCode.Conflict,
            "Customer enrollment is already in progress. Retry shortly.");
    }

    private async Task<SubscriptionDto> WaitForSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                var subscription = await _maxio.FindSubscriptionAsync(reference, cancellationToken);
                if (subscription is not null) return subscription;
                await Task.Delay(ReconciliationDelay, cancellationToken);
            }
        }
        catch (MaxioProviderException ex)
        {
            throw Translate(ex);
        }

        throw new SubscriptionBillingException(
            HttpStatusCode.Conflict,
            "The subscription enrollment is still in progress. Retry shortly.");
    }

    private async Task<MaxioCustomer> WaitForCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var customer = await _maxio.FindCustomerAsync(reference, cancellationToken);
            if (customer is not null) return customer;
            await Task.Delay(ReconciliationDelay, cancellationToken);
        }

        throw new SubscriptionBillingException(
            HttpStatusCode.Conflict,
            "Customer enrollment is still in progress. Retry shortly.");
    }

    private async Task SaveCompletionAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogInformation(ex, "A concurrent request already persisted the Maxio idempotency result.");
        }
    }

    private async Task TrySaveUncertainAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Could not persist the uncertain Maxio operation state.");
        }
    }

    private void DetachChangedEntries()
    {
        foreach (var entry in _db.ChangeTracker.Entries().Where(entry => entry.State != EntityState.Unchanged))
        {
            entry.State = EntityState.Detached;
        }
    }

    private SubscriptionBillingException Translate(MaxioProviderException exception)
    {
        _logger.LogWarning(
            exception,
            "Maxio billing failure {FailureKind} with provider status {ProviderStatus}",
            exception.Kind,
            exception.ProviderStatus is null ? null : (int)exception.ProviderStatus);

        var status = exception.ProviderStatus switch
        {
            HttpStatusCode.BadRequest => HttpStatusCode.BadRequest,
            HttpStatusCode.NotFound => HttpStatusCode.NotFound,
            HttpStatusCode.Conflict => HttpStatusCode.Conflict,
            HttpStatusCode.UnprocessableEntity => HttpStatusCode.UnprocessableEntity,
            HttpStatusCode.ServiceUnavailable => HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.TooManyRequests => HttpStatusCode.ServiceUnavailable,
            _ => HttpStatusCode.BadGateway
        };

        if (exception.Kind is MaxioFailureKind.Transport or MaxioFailureKind.AmbiguousWrite)
        {
            status = HttpStatusCode.ServiceUnavailable;
        }

        return new SubscriptionBillingException(status, exception.Message, exception);
    }

    private static string StableReference(string kind, params string[] values)
    {
        var input = string.Join('|', values);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return $"eshop-{kind}-{Convert.ToHexString(hash.AsSpan(0, 16)).ToLowerInvariant()}";
    }
}
