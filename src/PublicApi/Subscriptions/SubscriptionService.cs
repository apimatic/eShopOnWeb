using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionService
{
    private readonly IMaxioBillingClient _maxio;
    private readonly CatalogContext _catalogContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SubscriptionOperationLock _operationLock;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(
        IMaxioBillingClient maxio,
        CatalogContext catalogContext,
        UserManager<ApplicationUser> userManager,
        SubscriptionOperationLock operationLock,
        ILogger<SubscriptionService> logger)
    {
        _maxio = maxio;
        _catalogContext = catalogContext;
        _userManager = userManager;
        _operationLock = operationLock;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var plans = await _maxio.ListPlansAsync(cancellationToken);
        return plans.Select(plan => new SubscriptionPlanDto(
            plan.Handle,
            plan.Name,
            plan.PriceInCents / 100m,
            "USD",
            plan.Interval,
            plan.IntervalUnit,
            plan.PricePointHandle)).ToArray();
    }

    public async Task<SubscriptionDto> SubscribeAsync(ClaimsPrincipal principal, string? requestedPlanHandle, CancellationToken cancellationToken)
    {
        var userName = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
            throw new SubscriptionUnauthorizedException();

        var user = await _userManager.FindByNameAsync(userName);
        if (user is null || string.IsNullOrWhiteSpace(user.Email))
            throw new SubscriptionUnauthorizedException();

        var planHandle = requestedPlanHandle?.Trim();
        if (string.IsNullOrWhiteSpace(planHandle))
            throw new SubscriptionValidationException("planHandle is required. Select a plan from GET /api/subscription-plans.");

        var plans = await _maxio.ListPlansAsync(cancellationToken);
        var plan = plans.SingleOrDefault(x => string.Equals(x.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
            throw new SubscriptionValidationException("The requested plan is not available in the configured Maxio product family.");

        var operationKey = $"{user.Id}:{plan.Handle}";
        await using var lease = await AsyncSemaphoreLease.AcquireAsync(_operationLock.Get(operationKey), cancellationToken);

        var customerReference = CustomerReference(user.Id);
        var subscriptionReference = SubscriptionReference(user.Id, plan.Handle);
        var customer = await EnsureCustomerAsync(customerReference, user, cancellationToken);

        var record = await _catalogContext.MaxioSubscriptionRecords
            .SingleOrDefaultAsync(x => x.UserId == user.Id && x.PlanHandle == plan.Handle, cancellationToken);

        if (record is null)
        {
            record = new MaxioSubscriptionRecord(user.Id, plan.Handle, subscriptionReference);
            _catalogContext.MaxioSubscriptionRecords.Add(record);
            try
            {
                await _catalogContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                _catalogContext.Entry(record).State = EntityState.Detached;
                record = await _catalogContext.MaxioSubscriptionRecords
                    .SingleAsync(x => x.UserId == user.Id && x.PlanHandle == plan.Handle, cancellationToken);
            }
        }

        // A lookup before creation makes a retry safe even if the process died after Maxio
        // created the subscription but before the local correlation row was updated.
        var existing = await _maxio.FindSubscriptionAsync(record.SubscriptionReference, cancellationToken);
        if (existing is null)
        {
            existing = await _maxio.CreateSubscriptionAsync(
                customerReference,
                record.SubscriptionReference,
                plan.Handle,
                cancellationToken);
        }

        if (record.MaxioSubscriptionId != existing.Id || record.MaxioCustomerId != customer.Id)
        {
            record.AttachMaxioIds(customer.Id, existing.Id);
            await _catalogContext.SaveChangesAsync(cancellationToken);
        }

        _logger.LogInformation("Provisioned Maxio subscription {SubscriptionId} for eShop user {UserId}.", existing.Id, user.Id);
        return ToDto(existing, plan.Handle, plan.Name);
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var userName = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
            throw new SubscriptionUnauthorizedException();

        var user = await _userManager.FindByNameAsync(userName);
        if (user is null)
            throw new SubscriptionUnauthorizedException();

        var customer = await _maxio.FindCustomerAsync(CustomerReference(user.Id), cancellationToken);
        if (customer is null)
            return Array.Empty<SubscriptionDto>();

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(subscription => ToDto(
            subscription,
            subscription.ProductHandle ?? "unknown",
            subscription.ProductName)).ToArray();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(string reference, ApplicationUser user, CancellationToken cancellationToken)
    {
        var customer = await _maxio.FindCustomerAsync(reference, cancellationToken);
        if (customer is not null)
            return customer;

        try
        {
            return await _maxio.CreateCustomerAsync(
                reference,
                user.UserName?.Split('@')[0] ?? "eShop",
                "Shopper",
                user.Email!,
                cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
        {
            // Customer references are unique in Maxio. If another host won the race,
            // resolve the already-created customer instead of creating another one.
            customer = await _maxio.FindCustomerAsync(reference, cancellationToken);
            if (customer is not null)
                return customer;
            throw;
        }
    }

    private static string CustomerReference(string userId) => $"eshop-user:{userId}";

    private static string SubscriptionReference(string userId, string planHandle) =>
        $"eshop-subscription:{userId}:{planHandle}";

    private static SubscriptionDto ToDto(MaxioSubscription subscription, string planHandle, string? planName) => new(
        subscription.Id,
        planHandle,
        planName,
        subscription.State,
        subscription.PriceInCents / 100m,
        "USD",
        subscription.CurrentPeriodEndsAt,
        subscription.NextAssessmentAt,
        subscription.Reference ?? string.Empty);
}

public sealed class SubscriptionUnauthorizedException : Exception;

public sealed class SubscriptionValidationException : Exception
{
    public SubscriptionValidationException(string message) : base(message) { }
}

internal sealed class AsyncSemaphoreLease : IAsyncDisposable
{
    private readonly SemaphoreSlim _semaphore;

    private AsyncSemaphoreLease(SemaphoreSlim semaphore) => _semaphore = semaphore;

    public static async Task<AsyncSemaphoreLease> AcquireAsync(SemaphoreSlim semaphore, CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken);
        return new AsyncSemaphoreLease(semaphore);
    }

    public ValueTask DisposeAsync()
    {
        _semaphore.Release();
        return ValueTask.CompletedTask;
    }
}
