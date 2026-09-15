using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Subscriptions.Maxio;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionService : ISubscriptionService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscriptionLocks = new();
    private readonly IMaxioClient _maxio;
    private readonly AppIdentityDbContext _identityDb;
    private readonly MaxioOptions _maxioOptions;

    public SubscriptionService(IMaxioClient maxio, AppIdentityDbContext identityDb, IOptions<MaxioOptions> maxioOptions)
    {
        _maxio = maxio;
        _identityDb = identityDb;
        _maxioOptions = maxioOptions.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var products = await _maxio.ListProductsAsync(cancellationToken);
        return products
            .Where(product => product.ArchivedAt == null &&
                              !string.IsNullOrWhiteSpace(product.Handle) &&
                              string.Equals(product.ProductFamily?.Handle, _maxioOptions.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
            .OrderBy(product => product.Name)
            .Select(MapPlan)
            .ToList();
    }

    public async Task<SubscriptionDto> SubscribeAsync(ApplicationUser user, string planHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
            throw new SubscriptionPlanNotFoundException(planHandle);

        var plan = (await ListPlansAsync(cancellationToken))
            .SingleOrDefault(item => string.Equals(item.Handle, planHandle.Trim(), StringComparison.OrdinalIgnoreCase));
        if (plan == null)
            throw new SubscriptionPlanNotFoundException(planHandle);

        var userId = user.Id;
        var reference = BuildSubscriptionReference(userId, plan.Handle);
        var gate = SubscriptionLocks.GetOrAdd(reference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(user, cancellationToken);
            var link = await GetOrReserveLinkAsync(userId, plan.Handle, reference, customer.Id, cancellationToken);

            if (link.SubscriptionId.HasValue)
            {
                var existing = await _maxio.FindSubscriptionByReferenceAsync(reference, cancellationToken);
                if (existing != null)
                {
                    return MapSubscription(existing, plan);
                }

                link.SubscriptionId = null;
                link.Status = MaxioSubscriptionLink.PendingStatus;
                link.ProcessingToken = Guid.NewGuid().ToString("N");
                link.ProcessingUntil = DateTimeOffset.UtcNow.AddMinutes(5);
                link.UpdatedAt = DateTimeOffset.UtcNow;
                await _identityDb.SaveChangesAsync(cancellationToken);
            }

            var foundAfterReservation = await _maxio.FindSubscriptionByReferenceAsync(reference, cancellationToken);
            MaxioSubscription subscription;
            if (foundAfterReservation != null)
            {
                subscription = foundAfterReservation;
            }
            else
            {
                try
                {
                    subscription = await _maxio.CreateSubscriptionAsync(
                        new MaxioCreateSubscription
                        {
                            ProductHandle = plan.Handle,
                            CustomerId = customer.Id,
                            PaymentCollectionMethod = "remittance",
                            Reference = reference
                        }, cancellationToken);
                }
                catch (MaxioApiException exception) when ((int)exception.StatusCode == 422)
                {
                    // If another instance won the race, recover its subscription by the deterministic reference.
                    var createdByConcurrentRequest = await _maxio.FindSubscriptionByReferenceAsync(reference, cancellationToken);
                    if (createdByConcurrentRequest == null)
                        throw;
                    subscription = createdByConcurrentRequest;
                }
            }

            link.CustomerId = customer.Id;
            link.SubscriptionId = subscription.Id;
            link.Status = MaxioSubscriptionLink.ActiveStatus;
            link.ProcessingToken = null;
            link.ProcessingUntil = null;
            link.UpdatedAt = DateTimeOffset.UtcNow;
            await _identityDb.SaveChangesAsync(cancellationToken);

            return MapSubscription(subscription, plan);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var customer = await EnsureCustomerAsync(user, cancellationToken);
        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(subscription => MapSubscription(subscription, null)).ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var reference = BuildCustomerReference(user.Id);
        var existing = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing != null)
            return existing;

        var email = user.Email ?? user.UserName;
        if (string.IsNullOrWhiteSpace(email))
            throw new InvalidOperationException("The authenticated eShop user does not have an email address.");

        try
        {
            return await _maxio.CreateCustomerAsync(
                new MaxioCreateCustomer
                {
                    FirstName = "eShopOnWeb",
                    LastName = "Shopper",
                    Email = email,
                    Reference = reference
                }, cancellationToken);
        }
        catch (MaxioApiException exception) when ((int)exception.StatusCode == 422)
        {
            // The unique customer reference makes a concurrent create safe; recover the winner.
            var createdByConcurrentRequest = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
            if (createdByConcurrentRequest != null)
                return createdByConcurrentRequest;
            throw;
        }
    }

    private async Task<MaxioSubscriptionLink> GetOrReserveLinkAsync(
        string userId,
        string planHandle,
        string reference,
        int customerId,
        CancellationToken cancellationToken)
    {
        var ownsNewReservation = false;
        var link = await _identityDb.MaxioSubscriptionLinks
            .SingleOrDefaultAsync(item => item.UserId == userId && item.PlanHandle == planHandle, cancellationToken);

        if (link == null)
        {
            link = new MaxioSubscriptionLink
            {
                UserId = userId,
                PlanHandle = planHandle,
                SubscriptionReference = reference,
                CustomerId = customerId,
                Status = MaxioSubscriptionLink.PendingStatus,
                ProcessingToken = Guid.NewGuid().ToString("N"),
                ProcessingUntil = DateTimeOffset.UtcNow.AddMinutes(5),
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            _identityDb.MaxioSubscriptionLinks.Add(link);
            try
            {
                await _identityDb.SaveChangesAsync(cancellationToken);
                ownsNewReservation = true;
            }
            catch (DbUpdateException)
            {
                _identityDb.ChangeTracker.Clear();
                link = await _identityDb.MaxioSubscriptionLinks
                    .SingleAsync(item => item.UserId == userId && item.PlanHandle == planHandle, cancellationToken);
            }
        }

        if (link.SubscriptionId.HasValue)
            return link;

        if (!ownsNewReservation && link.ProcessingUntil.HasValue && link.ProcessingUntil > DateTimeOffset.UtcNow)
        {
            // Another app instance owns the reservation. Its Maxio reference is the recovery key.
            for (var attempt = 0; attempt < 20; attempt++)
            {
                var existing = await _maxio.FindSubscriptionByReferenceAsync(reference, cancellationToken);
                if (existing != null)
                {
                    link.CustomerId = customerId;
                    link.SubscriptionId = existing.Id;
                    link.Status = MaxioSubscriptionLink.ActiveStatus;
                    link.ProcessingToken = null;
                    link.ProcessingUntil = null;
                    link.UpdatedAt = DateTimeOffset.UtcNow;
                    await _identityDb.SaveChangesAsync(cancellationToken);
                    return link;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
            }

            throw new SubscriptionOperationInProgressException();
        }

        link.CustomerId = customerId;
        link.ProcessingToken = Guid.NewGuid().ToString("N");
        link.ProcessingUntil = DateTimeOffset.UtcNow.AddMinutes(5);
        link.UpdatedAt = DateTimeOffset.UtcNow;
        await _identityDb.SaveChangesAsync(cancellationToken);
        return link;
    }

    private static SubscriptionPlanDto MapPlan(MaxioProduct product) => new()
    {
        Handle = product.Handle!,
        Name = product.Name,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit,
        TrialPriceInCents = product.TrialPriceInCents,
        TrialInterval = product.TrialInterval,
        TrialIntervalUnit = product.TrialIntervalUnit
    };

    private static SubscriptionDto MapSubscription(MaxioSubscription subscription, SubscriptionPlanDto? plan) => new()
    {
        Id = subscription.Id,
        PlanHandle = subscription.Product?.Handle ?? plan?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? plan?.Name ?? string.Empty,
        PriceInCents = subscription.ProductPriceInCents,
        State = subscription.State,
        NextBillingDate = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt
    };

    private static string BuildCustomerReference(string userId) => $"eshop-user/{userId}";
    private static string BuildSubscriptionReference(string userId, string planHandle) => $"eshop-subscription/{userId}/{planHandle}";
}
