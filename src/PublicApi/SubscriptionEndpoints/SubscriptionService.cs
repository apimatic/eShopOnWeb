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
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscriptionLocks = new();
    private static readonly TimeSpan PendingLease = TimeSpan.FromMinutes(2);

    private readonly IMaxioClient _maxio;
    private readonly AppIdentityDbContext _identityDb;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly MaxioOptions _options;

    public SubscriptionService(
        IMaxioClient maxio,
        AppIdentityDbContext identityDb,
        UserManager<ApplicationUser> userManager,
        IOptions<MaxioOptions> options)
    {
        _maxio = maxio;
        _identityDb = identityDb;
        _userManager = userManager;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var products = await _maxio.ListProductsAsync(_options.ProductFamilyHandle, cancellationToken);
        return products
            .Where(x => x.ArchivedAt is null)
            .OrderBy(x => x.PriceInCents)
            .Select(ToPlanDto)
            .ToArray();
    }

    public async Task<(SubscriptionDto Subscription, bool Created)> SubscribeAsync(
        ClaimsPrincipal principal,
        string productHandle,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new ArgumentException("productHandle is required.", nameof(productHandle));
        }

        var user = await GetUserAsync(principal);
        var normalizedHandle = productHandle.Trim();
        var plans = await _maxio.ListProductsAsync(_options.ProductFamilyHandle, cancellationToken);
        var product = plans.SingleOrDefault(x =>
            x.ArchivedAt is null && string.Equals(x.Handle, normalizedHandle, StringComparison.Ordinal));
        if (product is null)
        {
            throw new KeyNotFoundException("The requested subscription plan was not found.");
        }

        var lockKey = $"{user.Id}\n{normalizedHandle}";
        var gate = SubscriptionLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var reference = CreateSubscriptionReference(user.Id, normalizedHandle);
            var (record, ownsAttempt) = await ReserveAsync(user.Id, normalizedHandle, reference, cancellationToken);
            if (!ownsAttempt && string.Equals(record.Status, SubscriptionRecordStatus.Pending, StringComparison.Ordinal))
            {
                throw new SubscriptionInProgressException();
            }

            try
            {
                var customer = await EnsureCustomerAsync(user, cancellationToken);
                var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
                var subscription = subscriptions.SingleOrDefault(x =>
                    string.Equals(x.Reference, reference, StringComparison.Ordinal));

                if (subscription is not null)
                {
                    await MarkSucceededAsync(record, customer.Id, subscription.Id, cancellationToken);
                    return (ToSubscriptionDto(subscription), false);
                }

                subscription = await _maxio.CreateSubscriptionAsync(
                    new MaxioCreateSubscription
                    {
                        ProductHandle = normalizedHandle,
                        CustomerId = customer.Id,
                        Reference = reference
                    },
                    cancellationToken);

                await MarkSucceededAsync(record, customer.Id, subscription.Id, cancellationToken);
                return (ToSubscriptionDto(subscription), true);
            }
            catch (Exception exception)
            {
                // Only an explicit Maxio 4xx proves that enrollment was rejected. A timeout,
                // 5xx, cancellation, or local persistence failure may follow a successful POST;
                // retain the lease so a later retry reconciles by reference before creating.
                if (exception is MaxioApiException maxioException
                    && (int)maxioException.StatusCode is >= 400 and < 500)
                {
                    record.Status = SubscriptionRecordStatus.Failed;
                    record.UpdatedAt = DateTimeOffset.UtcNow;
                    try
                    {
                        await _identityDb.SaveChangesAsync(CancellationToken.None);
                    }
                    catch (DbUpdateException)
                    {
                        // Preserve the original Maxio/application failure.
                    }
                }

                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListMineAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var user = await GetUserAsync(principal);
        var customer = await _maxio.FindCustomerByReferenceAsync(CreateCustomerReference(user.Id), cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions
            .Where(x => string.Equals(
                x.Product.ProductFamily.Handle,
                _options.ProductFamilyHandle,
                StringComparison.Ordinal))
            .OrderByDescending(x => x.Id)
            .Select(ToSubscriptionDto)
            .ToArray();
    }

    private async Task<ApplicationUser> GetUserAsync(ClaimsPrincipal principal)
    {
        var username = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new UnauthorizedAccessException("The bearer token does not identify a user.");
        }

        return await _userManager.FindByNameAsync(username)
            ?? throw new UnauthorizedAccessException("The bearer token user no longer exists.");
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(
        ApplicationUser user,
        CancellationToken cancellationToken)
    {
        var reference = CreateCustomerReference(user.Id);
        var existing = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var email = user.Email ?? user.UserName
            ?? throw new InvalidOperationException("The current user has no email address.");
        var (firstName, lastName) = CustomerNameFromEmail(email);

        try
        {
            return await _maxio.CreateCustomerAsync(
                new MaxioCreateCustomer
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = email,
                    Reference = reference
                },
                cancellationToken);
        }
        catch (MaxioApiException exception) when (exception.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Maxio enforces customer reference uniqueness. A competing request may have won.
            var concurrentlyCreated = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
            if (concurrentlyCreated is not null)
            {
                return concurrentlyCreated;
            }

            throw;
        }
    }

    private async Task<(SubscriptionRecord Record, bool OwnsAttempt)> ReserveAsync(
        string userId,
        string productHandle,
        string reference,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var record = await _identityDb.SubscriptionRecords.SingleOrDefaultAsync(
            x => x.UserId == userId && x.ProductHandle == productHandle,
            cancellationToken);

        if (record is null)
        {
            record = new SubscriptionRecord
            {
                UserId = userId,
                ProductHandle = productHandle,
                SubscriptionReference = reference,
                Status = SubscriptionRecordStatus.Pending,
                CreatedAt = now,
                UpdatedAt = now
            };
            _identityDb.SubscriptionRecords.Add(record);
            try
            {
                await _identityDb.SaveChangesAsync(cancellationToken);
                return (record, true);
            }
            catch (DbUpdateException)
            {
                _identityDb.ChangeTracker.Clear();
                record = await _identityDb.SubscriptionRecords.SingleAsync(
                    x => x.UserId == userId && x.ProductHandle == productHandle,
                    cancellationToken);
            }
        }

        if (string.Equals(record.Status, SubscriptionRecordStatus.Succeeded, StringComparison.Ordinal))
        {
            return (record, true);
        }

        if (string.Equals(record.Status, SubscriptionRecordStatus.Pending, StringComparison.Ordinal)
            && record.UpdatedAt > now.Subtract(PendingLease))
        {
            return (record, false);
        }

        record.Status = SubscriptionRecordStatus.Pending;
        record.UpdatedAt = now;
        try
        {
            await _identityDb.SaveChangesAsync(cancellationToken);
            return (record, true);
        }
        catch (DbUpdateConcurrencyException)
        {
            _identityDb.ChangeTracker.Clear();
            record = await _identityDb.SubscriptionRecords.SingleAsync(
                x => x.UserId == userId && x.ProductHandle == productHandle,
                cancellationToken);
            if (string.Equals(record.Status, SubscriptionRecordStatus.Pending, StringComparison.Ordinal))
            {
                return (record, false);
            }

            if (string.Equals(record.Status, SubscriptionRecordStatus.Succeeded, StringComparison.Ordinal))
            {
                return (record, true);
            }

            return await ReserveAsync(userId, productHandle, reference, cancellationToken);
        }
    }

    private async Task MarkSucceededAsync(
        SubscriptionRecord record,
        long customerId,
        long subscriptionId,
        CancellationToken cancellationToken)
    {
        record.MaxioCustomerId = customerId;
        record.MaxioSubscriptionId = subscriptionId;
        record.Status = SubscriptionRecordStatus.Succeeded;
        record.UpdatedAt = DateTimeOffset.UtcNow;
        await _identityDb.SaveChangesAsync(cancellationToken);
    }

    private static string CreateCustomerReference(string userId) => $"eshop-user:{userId}";

    private static string CreateSubscriptionReference(string userId, string productHandle)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{userId}\n{productHandle}"));
        return $"eshop-sub:{Convert.ToHexString(bytes).ToLowerInvariant()[..40]}";
    }

    private static (string FirstName, string LastName) CustomerNameFromEmail(string email)
    {
        var localPart = email.Split('@', 2)[0];
        var parts = localPart.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            >= 2 => (parts[0], string.Join(' ', parts.Skip(1))),
            1 => (parts[0], "eShopOnWeb"),
            _ => ("eShopOnWeb", "Customer")
        };
    }

    private static SubscriptionPlanDto ToPlanDto(MaxioProduct product) => new()
    {
        Handle = product.Handle ?? string.Empty,
        Name = product.Name,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit,
        RequiresPaymentMethod = product.RequireCreditCard
    };

    private static SubscriptionDto ToSubscriptionDto(MaxioSubscription subscription) => new()
    {
        Id = subscription.Id,
        PlanHandle = subscription.Product.Handle ?? string.Empty,
        PlanName = subscription.Product.Name,
        PriceInCents = subscription.ProductPriceInCents,
        Currency = subscription.Currency,
        Interval = subscription.Product.Interval,
        IntervalUnit = subscription.Product.IntervalUnit,
        State = subscription.State,
        // The spec defines current_period_ends_at as the next regular attempted charge.
        NextBillingAt = subscription.CurrentPeriodEndsAt
    };
}
