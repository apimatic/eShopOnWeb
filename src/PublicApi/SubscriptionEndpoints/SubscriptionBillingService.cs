using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> RequestLocks = new();
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PeerWait = TimeSpan.FromSeconds(20);

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AppIdentityDbContext _identityContext;
    private readonly IMaxioBillingGateway _gateway;

    public SubscriptionBillingService(
        IHttpContextAccessor httpContextAccessor,
        UserManager<ApplicationUser> userManager,
        AppIdentityDbContext identityContext,
        IMaxioBillingGateway gateway)
    {
        _httpContextAccessor = httpContextAccessor;
        _userManager = userManager;
        _identityContext = identityContext;
        _gateway = gateway;
    }

    public Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken) =>
        _gateway.GetPlansAsync(cancellationToken);

    public async Task<SubscriptionDto> SubscribeAsync(
        string productHandle,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        ValidateProductHandle(productHandle);
        ValidateIdempotencyKey(idempotencyKey);

        var profile = await GetCurrentProfileAsync();
        await _gateway.GetPlanAsync(productHandle, cancellationToken);
        var customer = await _gateway.EnsureCustomerAsync(profile, cancellationToken);

        var lockKey = $"{profile.StableUserId}:{idempotencyKey}";
        var requestLock = RequestLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await requestLock.WaitAsync(cancellationToken);
        try
        {
            var owner = Guid.NewGuid().ToString("N");
            var record = await GetOrCreateRequestAsync(
                profile.StableUserId, idempotencyKey, productHandle, owner, cancellationToken);

            if (!string.Equals(record.ProductHandle, productHandle, StringComparison.Ordinal))
            {
                throw BillingException.Conflict(
                    "That idempotency key was already used with a different product handle.");
            }

            if (record.Status == SubscriptionRequestStatus.Completed)
            {
                return await ReadCompletedAsync(record, cancellationToken);
            }

            if (record.Status == SubscriptionRequestStatus.OutcomeUnknown)
            {
                return await ReconcileUnknownAsync(record, cancellationToken);
            }

            if (!string.Equals(record.LeaseOwner, owner, StringComparison.Ordinal))
            {
                record = await WaitForPeerAsync(record.Id, cancellationToken);
                if (record.Status == SubscriptionRequestStatus.Completed)
                {
                    return await ReadCompletedAsync(record, cancellationToken);
                }

                if (record.Status == SubscriptionRequestStatus.OutcomeUnknown)
                {
                    return await ReconcileUnknownAsync(record, cancellationToken);
                }

                if (record.LeaseExpiresAt > DateTimeOffset.UtcNow)
                {
                    throw BillingException.UnknownOutcome();
                }

                record.LeaseOwner = owner;
                record.LeaseExpiresAt = DateTimeOffset.UtcNow.Add(LeaseDuration);
                record.UpdatedAt = DateTimeOffset.UtcNow;
                await SaveLeaseAsync(cancellationToken);
            }

            var recovered = await _gateway.FindSubscriptionAsync(record.ProviderReference, cancellationToken);
            if (recovered != null)
            {
                await CompleteAsync(record, recovered, cancellationToken);
                return recovered;
            }

            try
            {
                var created = await _gateway.CreateSubscriptionAsync(
                    productHandle,
                    customer.Reference,
                    record.ProviderReference,
                    cancellationToken);
                await CompleteAsync(record, created, cancellationToken);
                return created;
            }
            catch (BillingException ex) when ((int)ex.StatusCode >= 500)
            {
                await MarkUnknownAsync(record, cancellationToken);
                throw;
            }
            catch (BillingException)
            {
                _identityContext.SubscriptionRequests.Remove(record);
                await _identityContext.SaveChangesAsync(cancellationToken);
                throw;
            }
        }
        finally
        {
            requestLock.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(
        CancellationToken cancellationToken)
    {
        var profile = await GetCurrentProfileAsync();
        return await _gateway.GetCustomerSubscriptionsAsync(
            CustomerReference(profile.StableUserId), cancellationToken);
    }

    private async Task<BillingCustomerProfile> GetCurrentProfileAsync()
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true)
        {
            throw new BillingException(HttpStatusCode.Unauthorized, "Authentication required",
                "A valid bearer token is required.");
        }

        var stableId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var userName = principal.Identity.Name;
        ApplicationUser? user = null;
        if (!string.IsNullOrWhiteSpace(stableId))
        {
            user = await _userManager.FindByIdAsync(stableId);
        }

        if (user == null && !string.IsNullOrWhiteSpace(userName))
        {
            user = await _userManager.FindByNameAsync(userName);
        }

        if (user == null)
        {
            throw new BillingException(HttpStatusCode.Unauthorized, "Unknown account",
                "The authenticated account no longer exists.");
        }

        if (string.IsNullOrWhiteSpace(user.Email) ||
            string.IsNullOrWhiteSpace(user.FirstName) ||
            string.IsNullOrWhiteSpace(user.LastName))
        {
            throw BillingException.Conflict(
                "The account profile must include an email address, first name, and last name before subscribing.");
        }

        return new BillingCustomerProfile(user.Id, user.Email, user.FirstName, user.LastName);
    }

    private async Task<SubscriptionRequest> GetOrCreateRequestAsync(
        string userId,
        string idempotencyKey,
        string productHandle,
        string owner,
        CancellationToken cancellationToken)
    {
        var existing = await _identityContext.SubscriptionRequests
            .SingleOrDefaultAsync(request => request.UserId == userId &&
                request.IdempotencyKey == idempotencyKey, cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        var now = DateTimeOffset.UtcNow;
        var request = new SubscriptionRequest
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            IdempotencyKey = idempotencyKey,
            ProductHandle = productHandle,
            ProviderReference = SubscriptionReference(userId, idempotencyKey),
            Status = SubscriptionRequestStatus.InProgress,
            LeaseOwner = owner,
            LeaseExpiresAt = now.Add(LeaseDuration),
            CreatedAt = now,
            UpdatedAt = now
        };

        _identityContext.SubscriptionRequests.Add(request);
        try
        {
            await _identityContext.SaveChangesAsync(cancellationToken);
            return request;
        }
        catch (DbUpdateException)
        {
            _identityContext.ChangeTracker.Clear();
            var raced = await _identityContext.SubscriptionRequests
                .SingleOrDefaultAsync(candidate => candidate.UserId == userId &&
                    candidate.IdempotencyKey == idempotencyKey, cancellationToken);
            if (raced == null)
            {
                throw;
            }

            return raced;
        }
    }

    private async Task<SubscriptionRequest> WaitForPeerAsync(Guid requestId, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.Add(PeerWait);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
            _identityContext.ChangeTracker.Clear();
            var record = await _identityContext.SubscriptionRequests
                .SingleAsync(request => request.Id == requestId, cancellationToken);
            if (record.Status != SubscriptionRequestStatus.InProgress ||
                record.LeaseExpiresAt <= DateTimeOffset.UtcNow)
            {
                return record;
            }
        }

        _identityContext.ChangeTracker.Clear();
        return await _identityContext.SubscriptionRequests
            .SingleAsync(request => request.Id == requestId, cancellationToken);
    }

    private async Task<SubscriptionDto> ReadCompletedAsync(
        SubscriptionRequest record,
        CancellationToken cancellationToken)
    {
        var subscription = await _gateway.FindSubscriptionAsync(record.ProviderReference, cancellationToken);
        if (subscription == null)
        {
            await MarkUnknownAsync(record, cancellationToken);
            throw BillingException.UnknownOutcome();
        }

        return subscription;
    }

    private async Task<SubscriptionDto> ReconcileUnknownAsync(
        SubscriptionRequest record,
        CancellationToken cancellationToken)
    {
        var subscription = await _gateway.FindSubscriptionAsync(record.ProviderReference, cancellationToken);
        if (subscription == null)
        {
            throw BillingException.UnknownOutcome();
        }

        await CompleteAsync(record, subscription, cancellationToken);
        return subscription;
    }

    private async Task CompleteAsync(
        SubscriptionRequest record,
        SubscriptionDto subscription,
        CancellationToken cancellationToken)
    {
        record.ProviderSubscriptionId = subscription.Id;
        record.Status = SubscriptionRequestStatus.Completed;
        record.LeaseOwner = null;
        record.LeaseExpiresAt = null;
        record.UpdatedAt = DateTimeOffset.UtcNow;
        await _identityContext.SaveChangesAsync(cancellationToken);
    }

    private async Task MarkUnknownAsync(SubscriptionRequest record, CancellationToken cancellationToken)
    {
        record.Status = SubscriptionRequestStatus.OutcomeUnknown;
        record.LeaseOwner = null;
        record.LeaseExpiresAt = null;
        record.UpdatedAt = DateTimeOffset.UtcNow;
        await _identityContext.SaveChangesAsync(cancellationToken);
    }

    private async Task SaveLeaseAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _identityContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw BillingException.UnknownOutcome(ex);
        }
    }

    private static void ValidateProductHandle(string productHandle)
    {
        if (string.IsNullOrWhiteSpace(productHandle) || productHandle.Length > 255 ||
            productHandle.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
        {
            throw BillingException.InvalidRequest(
                "productHandle must contain 1-255 ASCII letters, digits, hyphens, or underscores.");
        }
    }

    private static void ValidateIdempotencyKey(string idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 128 ||
            idempotencyKey.Any(char.IsControl))
        {
            throw BillingException.InvalidRequest(
                "The Idempotency-Key header must contain 1-128 non-control characters.");
        }
    }

    private static string CustomerReference(string userId) => $"eshop-user:{userId}";

    private static string SubscriptionReference(string userId, string idempotencyKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{userId}\n{idempotencyKey}"));
        return $"eshop-sub:{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }
}
