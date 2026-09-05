using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionService
{
    private readonly MaxioApiClient _maxio;
    private readonly MaxioOptions _options;
    private readonly CatalogContext _catalogContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SubscriptionEnrollmentLock _enrollmentLock;

    public SubscriptionService(MaxioApiClient maxio, Microsoft.Extensions.Options.IOptions<MaxioOptions> options,
        CatalogContext catalogContext, UserManager<ApplicationUser> userManager, SubscriptionEnrollmentLock enrollmentLock)
    {
        _maxio = maxio;
        _options = options.Value;
        _catalogContext = catalogContext;
        _userManager = userManager;
        _enrollmentLock = enrollmentLock;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var products = await _maxio.ListProductsAsync(cancellationToken);
        return products
            .Where(IsAvailablePlan)
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => new SubscriptionPlanDto(x.Handle!, x.Name, x.PriceInCents, x.Interval, x.IntervalUnit))
            .ToArray();
    }

    public async Task<SubscriptionDto> SubscribeAsync(string username, string productHandle, CancellationToken cancellationToken)
    {
        var user = await GetUserAsync(username);
        if (string.IsNullOrWhiteSpace(productHandle)) throw new SubscriptionValidationException("productHandle is required.");

        await using var lease = await _enrollmentLock.AcquireAsync(user.Id, cancellationToken);
        var plans = await ListPlansAsync(cancellationToken);
        var selectedPlan = plans.FirstOrDefault(x => string.Equals(x.Handle, productHandle, StringComparison.Ordinal));
        if (selectedPlan is null) throw new SubscriptionValidationException("The selected plan is not available.");

        var customer = await EnsureCustomerAsync(user, cancellationToken);
        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var existing = subscriptions.FirstOrDefault(x =>
            string.Equals(x.Product?.Handle, productHandle, StringComparison.Ordinal) && IsCurrent(x.State));

        var subscription = existing ?? await _maxio.CreateSubscriptionAsync(customer.Id, productHandle, cancellationToken);
        await SaveSubscriptionCorrelationAsync(user.Id, subscription, cancellationToken);
        return ToDto(subscription, selectedPlan);
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(string username, CancellationToken cancellationToken)
    {
        var user = await GetUserAsync(username);
        var customer = await _maxio.FindCustomerByReferenceAsync(CustomerReference(user.Id), cancellationToken);
        if (customer is null) return Array.Empty<SubscriptionDto>();

        await SaveCustomerCorrelationAsync(user.Id, customer.Id, cancellationToken);
        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var plans = await ListPlansAsync(cancellationToken);
        var plansByHandle = plans.ToDictionary(x => x.Handle, StringComparer.Ordinal);
        var result = new List<SubscriptionDto>();
        foreach (var subscription in subscriptions)
        {
            if (subscription.Product?.Handle is not { } handle || !plansByHandle.TryGetValue(handle, out var plan)) continue;
            await SaveSubscriptionCorrelationAsync(user.Id, subscription, cancellationToken);
            result.Add(ToDto(subscription, plan));
        }

        return result;
    }

    private bool IsAvailablePlan(MaxioProduct product) =>
        product.ArchivedAt is null && product.Handle is not null &&
        string.Equals(product.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.Ordinal);

    private async Task<ApplicationUser> GetUserAsync(string username)
    {
        var user = await _userManager.FindByNameAsync(username);
        return user ?? throw new SubscriptionValidationException("The authenticated user no longer exists.");
    }

    private async Task<MaxioCustomerData> EnsureCustomerAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var reference = CustomerReference(user.Id);
        var customer = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (customer is null)
        {
            var email = user.Email;
            if (string.IsNullOrWhiteSpace(email) || !IsEmail(email))
            {
                throw new SubscriptionValidationException("An email address is required before subscribing.");
            }

            try
            {
                customer = await _maxio.CreateCustomerAsync(new MaxioCreateCustomer(CustomerFirstName(user), "eShopOnWeb", email, reference), cancellationToken);
            }
            catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                // A separate process may have won the unique-reference race. Read its result.
                customer = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
                if (customer is null) throw;
            }
        }

        await SaveCustomerCorrelationAsync(user.Id, customer.Id, cancellationToken);
        return customer;
    }

    private async Task SaveCustomerCorrelationAsync(string userId, long customerId, CancellationToken cancellationToken)
    {
        var record = await _catalogContext.MaxioCustomers.SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (record is null) _catalogContext.MaxioCustomers.Add(new MaxioCustomer(userId, customerId));
        else record.UpdateMaxioCustomerId(customerId);
        await _catalogContext.SaveChangesAsync(cancellationToken);
    }

    private async Task SaveSubscriptionCorrelationAsync(string userId, MaxioSubscriptionData subscription, CancellationToken cancellationToken)
    {
        var handle = subscription.Product?.Handle;
        if (string.IsNullOrWhiteSpace(handle)) return;
        var record = await _catalogContext.MaxioSubscriptions.SingleOrDefaultAsync(x => x.MaxioSubscriptionId == subscription.Id, cancellationToken);
        if (record is null) _catalogContext.MaxioSubscriptions.Add(new MaxioSubscription(userId, subscription.Id, handle));
        await _catalogContext.SaveChangesAsync(cancellationToken);
    }

    private static SubscriptionDto ToDto(MaxioSubscriptionData subscription, SubscriptionPlanDto plan) =>
        new(subscription.Id, plan.Handle, plan.Name, subscription.ProductPriceInCents, subscription.State,
            subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt);

    private static bool IsCurrent(string state) => !string.Equals(state, "canceled", StringComparison.OrdinalIgnoreCase) &&
                                                   !string.Equals(state, "expired", StringComparison.OrdinalIgnoreCase);
    private static string CustomerReference(string userId) => $"eshoponweb-user-{userId}";
    private static string CustomerFirstName(ApplicationUser user) =>
        string.IsNullOrWhiteSpace(user.UserName) ? "eShopOnWeb" : user.UserName.Split('@')[0];
    private static bool IsEmail(string email)
    {
        try { _ = new MailAddress(email); return true; }
        catch (FormatException) { return false; }
    }
}

public sealed class SubscriptionEnrollmentLock
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public async Task<IAsyncDisposable> AcquireAsync(string userId, CancellationToken cancellationToken)
    {
        var semaphore = _locks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        return new Releaser(semaphore);
    }

    private sealed class Releaser : IAsyncDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        public Releaser(SemaphoreSlim semaphore) => _semaphore = semaphore;
        public ValueTask DisposeAsync() { _semaphore.Release(); return ValueTask.CompletedTask; }
    }
}

public sealed class SubscriptionValidationException : Exception
{
    public SubscriptionValidationException(string message) : base(message) { }
}

public sealed record SubscriptionPlanDto(string Handle, string Name, long PriceInCents, int Interval, string IntervalUnit);
public sealed record SubscriptionDto(long Id, string ProductHandle, string ProductName, long PriceInCents, string State, DateTimeOffset? NextBillingAt);
