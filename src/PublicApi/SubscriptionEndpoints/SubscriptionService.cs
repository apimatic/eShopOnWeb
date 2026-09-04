using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MaxioRemoteSubscription = Microsoft.eShopWeb.PublicApi.Maxio.MaxioSubscription;
using MaxioSubscriptionRecord = Microsoft.eShopWeb.ApplicationCore.Entities.MaxioSubscription;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserLocks = new();
    private readonly CatalogContext _catalogContext;
    private readonly IMaxioBillingClient _maxio;

    public SubscriptionService(CatalogContext catalogContext, IMaxioBillingClient maxio)
    {
        _catalogContext = catalogContext;
        _maxio = maxio;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
    {
        var plans = await _maxio.ListPlansAsync(cancellationToken);
        return plans.Select(ToPlanDto).ToArray();
    }

    public async Task<SubscriptionDto> SubscribeAsync(
        ApplicationUser user,
        string planHandle,
        CancellationToken cancellationToken)
    {
        planHandle = planHandle?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new ArgumentException("planHandle is required.", nameof(planHandle));
        }

        var gate = UserLocks.GetOrAdd(user.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var local = await _catalogContext.MaxioSubscriptions
                .SingleOrDefaultAsync(x => x.UserId == user.Id, cancellationToken);

            if (local is not null)
            {
                if (!string.Equals(local.ProductHandle, planHandle, StringComparison.Ordinal))
                {
                    throw new SubscriptionConflictException("This account already has a subscription. Manage it in Maxio before choosing another plan.");
                }

                var remoteExisting = await _maxio.FindSubscriptionByReferenceAsync(
                    local.SubscriptionReference, cancellationToken);
                if (remoteExisting is not null)
                {
                    await UpdateLocalRecordAsync(local, remoteExisting, cancellationToken);
                    return ToSubscriptionDto(local);
                }

                // The local record is the durable duplicate guard. If the provider
                // record cannot be found, do not create a second subscription.
                return ToSubscriptionDto(local);
            }

            var userKey = GetStableUserKey(user);
            var reference = BuildReference("eshop-subscription", userKey);
            var remoteByReference = await _maxio.FindSubscriptionByReferenceAsync(reference, cancellationToken);
            if (remoteByReference is not null)
            {
                if (remoteByReference.Product?.Handle is { } recoveredPlan &&
                    !string.Equals(recoveredPlan, planHandle, StringComparison.Ordinal))
                {
                    throw new SubscriptionConflictException("This account already has a subscription on another plan.");
                }

                var recovered = await SaveLocalRecordAsync(user.Id, planHandle, remoteByReference, null, reference, cancellationToken);
                return ToSubscriptionDto(recovered);
            }

            var plans = await _maxio.ListPlansAsync(cancellationToken);
            var plan = plans.SingleOrDefault(x => string.Equals(x.Handle, planHandle, StringComparison.Ordinal));
            if (plan is null)
            {
                throw new SubscriptionConflictException("The requested subscription plan is not available.");
            }

            var customer = await EnsureCustomerAsync(user, cancellationToken);
            MaxioRemoteSubscription subscription;
            try
            {
                subscription = await _maxio.CreateSubscriptionAsync(
                    reference,
                    planHandle,
                    customer.Id,
                    BuildToken("subscription-remittance-v1", userKey),
                    cancellationToken);
            }
            catch (MaxioApiException ex) when (ex.StatusCode == 409)
            {
                // A second request with the same uniqueness token means the first
                // request may have completed. Recover by the documented reference lookup.
                subscription = await FindCreatedSubscriptionAsync(reference, cancellationToken)
                    ?? throw new MaxioApiException(409, "Maxio accepted a duplicate-prevented request, but the subscription is not available yet.");
            }

            var record = await SaveLocalRecordAsync(user.Id, planHandle, subscription, customer.Id, reference, cancellationToken);
            return ToSubscriptionDto(record);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(
        ApplicationUser user,
        CancellationToken cancellationToken)
    {
        var local = await _catalogContext.MaxioSubscriptions
            .SingleOrDefaultAsync(x => x.UserId == user.Id, cancellationToken);
        var customerReference = BuildReference("eshop-customer", GetStableUserKey(user));
        var customerId = local?.MaxioCustomerId;

        if (customerId is null or 0)
        {
            var customer = await _maxio.FindCustomerByReferenceAsync(customerReference, cancellationToken);
            if (customer is null)
            {
                return Array.Empty<SubscriptionDto>();
            }

            customerId = customer.Id;
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customerId.Value, cancellationToken);
        foreach (var subscription in subscriptions)
        {
            if (local is not null && local.MaxioSubscriptionId == subscription.Id)
            {
                await UpdateLocalRecordAsync(local, subscription, cancellationToken);
            }
        }

        return subscriptions.Select(subscription => ToSubscriptionDto(subscription, local)).ToArray();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var reference = BuildReference("eshop-customer", GetStableUserKey(user));
        var existing = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var email = user.Email ?? user.UserName;
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new InvalidOperationException("The authenticated eShopOnWeb user has no email address.");
        }

        var lastName = (user.UserName ?? email).Split('@')[0];
        if (string.IsNullOrWhiteSpace(lastName))
        {
            lastName = user.Id;
        }

        try
        {
            return await _maxio.CreateCustomerAsync(
                reference,
                "eShopOnWeb",
                lastName,
                email,
                BuildToken("customer", GetStableUserKey(user)),
                cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 409 || ex.StatusCode == 422)
        {
            // Customer reference is unique in Maxio. A concurrent creator can
            // therefore be safely recovered with the reference lookup.
            var recovered = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
            if (recovered is not null)
            {
                return recovered;
            }

            throw new MaxioApiException(ex.StatusCode, ex.Message);
        }
    }

    private async Task<MaxioRemoteSubscription?> FindCreatedSubscriptionAsync(
        string reference,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var subscription = await _maxio.FindSubscriptionByReferenceAsync(reference, cancellationToken);
            if (subscription is not null)
            {
                return subscription;
            }

            if (attempt < 2)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * (attempt + 1)), cancellationToken);
            }
        }

        return null;
    }

    private async Task<MaxioSubscriptionRecord> SaveLocalRecordAsync(
        string userId,
        string requestedPlanHandle,
        MaxioRemoteSubscription subscription,
        int? customerId,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        var record = new MaxioSubscriptionRecord
        {
            UserId = userId,
            MaxioCustomerId = customerId ?? subscription.Customer?.Id ?? 0,
            MaxioSubscriptionId = subscription.Id,
            SubscriptionReference = subscription.Reference ?? subscriptionReference,
            ProductHandle = subscription.Product?.Handle ?? requestedPlanHandle,
            ProductName = subscription.Product?.Name ?? requestedPlanHandle,
            PriceInCents = subscription.Product?.PriceInCents ?? subscription.PriceInCents,
            Interval = subscription.Product?.Interval ?? 1,
            IntervalUnit = subscription.Product?.IntervalUnit ?? "month",
            State = subscription.State,
            NextBillingAt = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt,
            CreatedAt = subscription.CreatedAt,
            UpdatedAt = subscription.UpdatedAt
        };

        _catalogContext.MaxioSubscriptions.Add(record);
        try
        {
            await _catalogContext.SaveChangesAsync(cancellationToken);
            return record;
        }
        catch (DbUpdateException)
        {
            _catalogContext.ChangeTracker.Clear();
            var existing = await _catalogContext.MaxioSubscriptions
                .SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            throw;
        }
    }

    private async Task UpdateLocalRecordAsync(
        MaxioSubscriptionRecord record,
        MaxioRemoteSubscription subscription,
        CancellationToken cancellationToken)
    {
        record.MaxioCustomerId = subscription.Customer?.Id ?? record.MaxioCustomerId;
        record.MaxioSubscriptionId = subscription.Id;
        record.ProductHandle = subscription.Product?.Handle ?? record.ProductHandle;
        record.ProductName = subscription.Product?.Name ?? record.ProductName;
        record.PriceInCents = subscription.Product?.PriceInCents ?? subscription.PriceInCents;
        record.Interval = subscription.Product?.Interval ?? record.Interval;
        record.IntervalUnit = subscription.Product?.IntervalUnit ?? record.IntervalUnit;
        record.State = subscription.State;
        record.NextBillingAt = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt;
        record.UpdatedAt = subscription.UpdatedAt;
        await _catalogContext.SaveChangesAsync(cancellationToken);
    }

    private static SubscriptionPlanDto ToPlanDto(MaxioProduct plan) => new()
    {
        Handle = plan.Handle!,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        RequireCreditCard = plan.RequireCreditCard,
        Taxable = plan.Taxable
    };

    private static SubscriptionDto ToSubscriptionDto(MaxioSubscriptionRecord record) => new()
    {
        Id = record.MaxioSubscriptionId,
        PlanHandle = record.ProductHandle,
        PlanName = record.ProductName,
        PriceInCents = record.PriceInCents,
        Interval = record.Interval,
        IntervalUnit = record.IntervalUnit,
        State = record.State,
        NextBillingAt = record.NextBillingAt
    };

    private static SubscriptionDto ToSubscriptionDto(MaxioRemoteSubscription subscription, MaxioSubscriptionRecord? local) => new()
    {
        Id = subscription.Id,
        PlanHandle = subscription.Product?.Handle ?? local?.ProductHandle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? local?.ProductName ?? string.Empty,
        PriceInCents = subscription.Product?.PriceInCents ?? subscription.PriceInCents,
        Interval = subscription.Product?.Interval ?? local?.Interval ?? 1,
        IntervalUnit = subscription.Product?.IntervalUnit ?? local?.IntervalUnit ?? "month",
        State = subscription.State,
        NextBillingAt = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt
    };

    private static string BuildReference(string prefix, string userId) => $"{prefix}:{userId}";

    private static string GetStableUserKey(ApplicationUser user) =>
        (user.UserName ?? user.Email ?? user.Id).Trim().ToLowerInvariant();

    private static string BuildToken(string operation, string userId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"eshop:{operation}:{userId}"));
        return new Guid(bytes.AsSpan(0, 16)).ToString();
    }
}
