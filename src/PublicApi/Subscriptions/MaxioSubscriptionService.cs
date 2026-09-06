using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioSubscriptionService
{
    private readonly IMaxioAdvancedBillingClient _maxio;
    private readonly AppIdentityDbContext _identityDb;
    private readonly SubscriptionEnrollmentCoordinator _coordinator;

    public MaxioSubscriptionService(
        IMaxioAdvancedBillingClient maxio,
        AppIdentityDbContext identityDb,
        SubscriptionEnrollmentCoordinator coordinator)
    {
        _maxio = maxio;
        _identityDb = identityDb;
        _coordinator = coordinator;
    }

    public Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken) => GetPlansCoreAsync(cancellationToken);

    public async Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var customer = await _maxio.FindCustomerByReferenceAsync(CustomerReference(user.Id), cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var subscriptions = await _maxio.GetCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(ToDto).ToList();
    }

    public async Task<SubscriptionDto> SubscribeAsync(ApplicationUser user, string productHandle, CancellationToken cancellationToken)
    {
        var plan = (await _maxio.GetPlansAsync(cancellationToken))
            .SingleOrDefault(x => string.Equals(x.Handle, productHandle, StringComparison.Ordinal));
        if (plan is null)
        {
            throw new SubscriptionPlanNotFoundException(productHandle);
        }

        return await _coordinator.RunAsync($"{user.Id}:{productHandle}", async () =>
        {
            var reservation = await GetOrCreateEnrollmentAsync(user.Id, productHandle, cancellationToken);
            var enrollment = reservation.Enrollment;

            if (!reservation.Created && enrollment.Status == MaxioSubscriptionEnrollment.Pending)
            {
                var pendingCustomer = await _maxio.FindCustomerByReferenceAsync(CustomerReference(user.Id), cancellationToken);
                if (pendingCustomer is not null)
                {
                    var pendingSubscriptions = await _maxio.GetCustomerSubscriptionsAsync(pendingCustomer.Id, cancellationToken);
                    var recovered = pendingSubscriptions.FirstOrDefault(subscription =>
                        string.Equals(subscription.Product?.Handle, productHandle, StringComparison.Ordinal)
                        && IsCurrent(subscription.State));
                    if (recovered is not null)
                    {
                        await CompleteEnrollmentAsync(enrollment, pendingCustomer.Id, recovered.Id, cancellationToken);
                        return ToDto(recovered);
                    }
                }

                // Another app instance owns a fresh database reservation. It may be between its
                // Maxio calls, so creating here would turn a double-click into two subscriptions.
                if (enrollment.CreatedAtUtc > DateTimeOffset.UtcNow.AddMinutes(-2))
                {
                    throw new SubscriptionEnrollmentInProgressException();
                }
            }

            var customer = await EnsureCustomerAsync(user, enrollment, cancellationToken);

            var currentSubscriptions = await _maxio.GetCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var existing = currentSubscriptions.FirstOrDefault(subscription =>
                string.Equals(subscription.Product?.Handle, productHandle, StringComparison.Ordinal)
                && IsCurrent(subscription.State));

            if (existing is not null)
            {
                await CompleteEnrollmentAsync(enrollment, customer.Id, existing.Id, cancellationToken);
                return ToDto(existing);
            }

            var subscription = await _maxio.CreateSubscriptionAsync(
                productHandle,
                customer.Id,
                SubscriptionReference(enrollment.Id),
                cancellationToken);

            await CompleteEnrollmentAsync(enrollment, customer.Id, subscription.Id, cancellationToken);
            return ToDto(subscription);
        }, cancellationToken);
    }

    private async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansCoreAsync(CancellationToken cancellationToken)
    {
        var plans = await _maxio.GetPlansAsync(cancellationToken);
        return plans.Select(plan => new SubscriptionPlanDto(plan.Handle, plan.Name, plan.Description, plan.PriceInCents, plan.Interval, plan.IntervalUnit)).ToList();
    }

    private async Task<EnrollmentReservation> GetOrCreateEnrollmentAsync(string userId, string productHandle, CancellationToken cancellationToken)
    {
        var existing = await _identityDb.Set<MaxioSubscriptionEnrollment>()
            .SingleOrDefaultAsync(x => x.UserId == userId && x.ProductHandle == productHandle, cancellationToken);
        if (existing is not null)
        {
            return new EnrollmentReservation(existing, false);
        }

        var enrollment = new MaxioSubscriptionEnrollment
        {
            UserId = userId,
            ProductHandle = productHandle
        };
        _identityDb.Add(enrollment);
        try
        {
            await _identityDb.SaveChangesAsync(cancellationToken);
            return new EnrollmentReservation(enrollment, true);
        }
        catch (DbUpdateException)
        {
            _identityDb.Entry(enrollment).State = EntityState.Detached;
            var concurrentReservation = await _identityDb.Set<MaxioSubscriptionEnrollment>()
                .SingleAsync(x => x.UserId == userId && x.ProductHandle == productHandle, cancellationToken);
            return new EnrollmentReservation(concurrentReservation, false);
        }
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(ApplicationUser user, MaxioSubscriptionEnrollment enrollment, CancellationToken cancellationToken)
    {
        var reference = CustomerReference(user.Id);
        var customer = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (customer is null)
        {
            try
            {
                customer = await _maxio.CreateCustomerAsync(ToCustomerDraft(user, reference), cancellationToken);
            }
            catch (MaxioApiException exception) when ((int)exception.StatusCode == 422)
            {
                // Maxio enforces uniqueness of the customer reference. A racing request may have won.
                customer = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken)
                    ?? throw new MaxioApiException(exception.StatusCode, "Maxio rejected the customer creation but the customer could not be recovered.");
            }
        }

        if (enrollment.MaxioCustomerId != customer.Id)
        {
            enrollment.MaxioCustomerId = customer.Id;
            await _identityDb.SaveChangesAsync(cancellationToken);
        }

        return customer;
    }

    private async Task CompleteEnrollmentAsync(MaxioSubscriptionEnrollment enrollment, long customerId, long subscriptionId, CancellationToken cancellationToken)
    {
        enrollment.MaxioCustomerId = customerId;
        enrollment.MaxioSubscriptionId = subscriptionId;
        enrollment.Status = MaxioSubscriptionEnrollment.Completed;
        enrollment.CompletedAtUtc = DateTimeOffset.UtcNow;
        await _identityDb.SaveChangesAsync(cancellationToken);
    }

    private static MaxioCustomerDraft ToCustomerDraft(ApplicationUser user, string reference)
    {
        if (string.IsNullOrWhiteSpace(user.Email))
        {
            throw new SubscriptionCustomerProfileException();
        }

        var name = user.UserName?.Split('@', StringSplitOptions.RemoveEmptyEntries)[0] ?? "Shopper";
        return new MaxioCustomerDraft(name, "Customer", user.Email, reference);
    }

    private static bool IsCurrent(string state) => !string.Equals(state, "canceled", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(state, "expired", StringComparison.OrdinalIgnoreCase);

    private static SubscriptionDto ToDto(MaxioSubscription subscription)
    {
        return new SubscriptionDto(
            subscription.Id,
            subscription.Product?.Handle ?? string.Empty,
            subscription.Product?.Name ?? "Catalog-independent subscription",
            subscription.ProductPriceInCents,
            subscription.State,
            subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt);
    }

    private static string CustomerReference(string userId) => $"eshopweb-user-{userId}";
    private static string SubscriptionReference(int enrollmentId) => $"eshopweb-subscription-{enrollmentId}";

    private sealed record EnrollmentReservation(MaxioSubscriptionEnrollment Enrollment, bool Created);
}

public sealed class SubscriptionEnrollmentCoordinator
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public async Task<T> RunAsync<T>(string key, Func<Task<T>> action, CancellationToken cancellationToken)
    {
        var gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await action();
        }
        finally
        {
            gate.Release();
        }
    }
}

public sealed record SubscriptionPlanDto(string Handle, string Name, string? Description, long PriceInCents, int Interval, string IntervalUnit);
public sealed record SubscriptionDto(long Id, string ProductHandle, string PlanName, long PriceInCents, string State, DateTimeOffset? NextBillingAt);
public sealed class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string productHandle) : base($"Subscription plan '{productHandle}' is not available.") { }
}
public sealed class SubscriptionEnrollmentInProgressException : Exception
{
    public SubscriptionEnrollmentInProgressException() : base("Subscription enrollment is already being processed.") { }
}
public sealed class SubscriptionCustomerProfileException : Exception
{
    public SubscriptionCustomerProfileException() : base("An email address is required before subscribing.") { }
}
