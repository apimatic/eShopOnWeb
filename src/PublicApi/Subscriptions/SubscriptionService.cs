using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public interface ISubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken);
    Task<SubscriptionDto> SubscribeAsync(ApplicationUser user, string productHandle, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(ApplicationUser user, CancellationToken cancellationToken);
}

public sealed class SubscriptionService : ISubscriptionService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> EnrollmentLocks = new();

    private readonly IMaxioBillingClient _maxio;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AppIdentityDbContext _identityContext;

    public SubscriptionService(IMaxioBillingClient maxio, UserManager<ApplicationUser> userManager, AppIdentityDbContext identityContext)
    {
        _maxio = maxio;
        _userManager = userManager;
        _identityContext = identityContext;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
    {
        var plans = await _maxio.ListPlansAsync(cancellationToken);
        return plans
            .Where(plan => plan.ArchivedAt is null && !string.IsNullOrWhiteSpace(plan.Handle))
            .Select(ToPlanDto)
            .OrderBy(plan => plan.PriceInCents)
            .ToList();
    }

    public async Task<SubscriptionDto> SubscribeAsync(ApplicationUser user, string productHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(productHandle)) throw new ArgumentException("ProductHandle is required.", nameof(productHandle));

        var normalizedHandle = productHandle.Trim();
        var product = await _maxio.GetPlanAsync(normalizedHandle, cancellationToken)
            ?? throw new ArgumentException("The requested subscription plan is unavailable.", nameof(productHandle));

        var reference = SubscriptionReference(user.Id, normalizedHandle);
        var gate = EnrollmentLocks.GetOrAdd(reference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var existing = await _maxio.FindSubscriptionAsync(reference, cancellationToken);
            if (existing is not null)
            {
                await PersistEnrollmentAsync(user.Id, normalizedHandle, existing.Id, cancellationToken);
                return ToSubscriptionDto(existing, product);
            }

            var customer = await EnsureCustomerAsync(user, cancellationToken);
            MaxioSubscription enrolled;
            try
            {
                enrolled = await _maxio.CreateSubscriptionAsync(
                    new CreateMaxioSubscription(normalizedHandle, customer.Id, reference, FirstBillingAt(product)), cancellationToken);
            }
            catch (MaxioApiException exception) when (exception.StatusCode == HttpStatusCode.UnprocessableEntity || exception.StatusCode == HttpStatusCode.Conflict)
            {
                // A competing request may have created this deterministic reference.
                var recovered = await _maxio.FindSubscriptionAsync(reference, cancellationToken);
                if (recovered is null) throw;
                enrolled = recovered;
            }

            await PersistEnrollmentAsync(user.Id, normalizedHandle, enrolled.Id, cancellationToken);
            return ToSubscriptionDto(enrolled, product);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var customer = await _maxio.FindCustomerAsync(CustomerReference(user.Id), cancellationToken);
        if (customer is null) return Array.Empty<SubscriptionDto>();

        await PersistCustomerIdAsync(user, customer.Id);
        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        foreach (var subscription in subscriptions.Where(subscription => !string.IsNullOrWhiteSpace(subscription.Product?.Handle)))
        {
            await PersistEnrollmentAsync(user.Id, subscription.Product!.Handle!, subscription.Id, cancellationToken);
        }

        return subscriptions.Select(subscription => ToSubscriptionDto(subscription, subscription.Product)).ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var reference = CustomerReference(user.Id);
        var customer = await _maxio.FindCustomerAsync(reference, cancellationToken);
        if (customer is null)
        {
            if (string.IsNullOrWhiteSpace(user.Email))
            {
                throw new InvalidOperationException("A verified email address is required before creating a subscription.");
            }

            var firstName = FirstName(user.UserName ?? user.Email);
            try
            {
                customer = await _maxio.CreateCustomerAsync(
                    new CreateMaxioCustomer(firstName, "Shopper", user.Email, reference), cancellationToken);
            }
            catch (MaxioApiException exception) when (exception.StatusCode == HttpStatusCode.UnprocessableEntity || exception.StatusCode == HttpStatusCode.Conflict)
            {
                var recovered = await _maxio.FindCustomerAsync(reference, cancellationToken);
                if (recovered is null) throw;
                customer = recovered;
            }
        }

        await PersistCustomerIdAsync(user, customer.Id);
        return customer;
    }

    private async Task PersistCustomerIdAsync(ApplicationUser user, int customerId)
    {
        if (user.MaxioCustomerId == customerId) return;
        user.MaxioCustomerId = customerId;
        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException("The Maxio customer mapping could not be saved.");
        }
    }

    private async Task PersistEnrollmentAsync(string userId, string productHandle, int subscriptionId, CancellationToken cancellationToken)
    {
        var enrollment = await _identityContext.MaxioSubscriptionEnrollments.SingleOrDefaultAsync(
            item => item.UserId == userId && item.ProductHandle == productHandle, cancellationToken);
        if (enrollment is null)
        {
            _identityContext.MaxioSubscriptionEnrollments.Add(new MaxioSubscriptionEnrollment
            {
                UserId = userId,
                ProductHandle = productHandle,
                MaxioSubscriptionId = subscriptionId,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                LastSyncedAtUtc = DateTimeOffset.UtcNow
            });
        }
        else
        {
            enrollment.MaxioSubscriptionId = subscriptionId;
            enrollment.LastSyncedAtUtc = DateTimeOffset.UtcNow;
        }

        try
        {
            await _identityContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _identityContext.ChangeTracker.Clear();
        }
    }

    private static SubscriptionPlanDto ToPlanDto(MaxioProduct product) => new(
        product.Handle ?? string.Empty,
        product.Name ?? product.Handle ?? "Subscription plan",
        product.Description,
        product.PriceInCents,
        product.Interval,
        product.IntervalUnit ?? "month");

    private static SubscriptionDto ToSubscriptionDto(MaxioSubscription subscription, MaxioProduct? fallbackProduct) => new(
        subscription.Id,
        subscription.Product?.Handle ?? fallbackProduct?.Handle ?? string.Empty,
        subscription.Product?.Name ?? fallbackProduct?.Name ?? "Subscription plan",
        subscription.ProductPriceInCents != 0 ? subscription.ProductPriceInCents : fallbackProduct?.PriceInCents ?? 0,
        subscription.State ?? "unknown",
        subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt);

    private static string CustomerReference(string userId) => $"eshop-customer-{Hash(userId)}";
    private static string SubscriptionReference(string userId, string productHandle) => $"eshop-subscription-{Hash($"{userId}\n{productHandle}")}";

    private static string Hash(string input) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)))[..32].ToLowerInvariant();

    private static string FirstName(string value)
    {
        var atIndex = value.IndexOf('@');
        var name = atIndex > 0 ? value[..atIndex] : value;
        return string.IsNullOrWhiteSpace(name) ? "Shopper" : name[..Math.Min(name.Length, 50)];
    }

    // A future next_billing_at is the documented Maxio API mechanism that creates the
    // subscription without attempting an immediate collection. Maxio returns the
    // authoritative billing date in the subscription response.
    private static DateTimeOffset FirstBillingAt(MaxioProduct product) =>
        string.Equals(product.IntervalUnit, "day", StringComparison.OrdinalIgnoreCase)
            ? DateTimeOffset.UtcNow.AddDays(product.Interval)
            : DateTimeOffset.UtcNow.AddMonths(product.Interval);
}

public sealed record SubscriptionPlanDto(string Handle, string Name, string? Description, long PriceInCents, int Interval, string IntervalUnit);
public sealed record SubscriptionDto(int Id, string ProductHandle, string ProductName, long PriceInCents, string State, DateTimeOffset? NextBillingAt);
