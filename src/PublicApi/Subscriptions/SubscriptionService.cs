using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit);

public sealed record SubscriptionDetails(
    int Id,
    string? PlanHandle,
    string? PlanName,
    long PriceInCents,
    string? State,
    DateTimeOffset? NextBillingDate);

public interface ISubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken);
    Task<SubscriptionDetails> SubscribeAsync(ApplicationUser user, string planHandle, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionDetails>> GetMySubscriptionsAsync(ApplicationUser user, CancellationToken cancellationToken);
}

public sealed class SubscriptionService : ISubscriptionService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> CreationLocks = new();
    private readonly IMaxioApiClient _maxio;
    private readonly AppIdentityDbContext _identityDb;
    private readonly MaxioOptions _options;

    public SubscriptionService(IMaxioApiClient maxio, AppIdentityDbContext identityDb, IOptions<MaxioOptions> options)
    {
        _maxio = maxio;
        _identityDb = identityDb;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken)
    {
        var products = await _maxio.ListProductsAsync(cancellationToken);
        return products
            .Where(IsConfiguredFamilyProduct)
            .Where(product => !string.IsNullOrWhiteSpace(product.Handle) && product.ArchivedAt is null)
            .Select(ToPlan)
            .OrderBy(plan => plan.PriceInCents)
            .ThenBy(plan => plan.Name)
            .ToList();
    }

    public async Task<SubscriptionDetails> SubscribeAsync(ApplicationUser user, string planHandle,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(user.Id) || string.IsNullOrWhiteSpace(user.Email))
            throw new SubscriptionRequestException("The authenticated user must have an email address.");

        var plans = await GetPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(candidate => string.Equals(candidate.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
            throw new SubscriptionRequestException("The requested subscription plan is not available.");

        // Serialize all billing work for a user so simultaneous signups for different
        // plans cannot race customer creation.
        var lockKey = user.Id;
        var creationLock = CreationLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await creationLock.WaitAsync(cancellationToken);
        try
        {
            return await SubscribeUnderLockAsync(user, plan, cancellationToken);
        }
        finally
        {
            creationLock.Release();
            if (creationLock.CurrentCount == 1)
                CreationLocks.TryRemove(new KeyValuePair<string, SemaphoreSlim>(lockKey, creationLock));
        }
    }

    public async Task<IReadOnlyList<SubscriptionDetails>> GetMySubscriptionsAsync(ApplicationUser user,
        CancellationToken cancellationToken)
    {
        var customerReference = CustomerReference(user);
        var mapping = await _identityDb.MaxioSubscriptionMappings
            .AsNoTracking()
            .Where(item => item.UserId == user.Id)
            .OrderBy(item => item.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var customer = await _maxio.FindCustomerByReferenceAsync(customerReference, cancellationToken)
            ?? (mapping is not null ? new MaxioCustomer { Id = mapping.MaxioCustomerId } : null);

        if (customer is null)
            return Array.Empty<SubscriptionDetails>();

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions
            .Where(subscription => subscription.Product is not null && IsConfiguredFamilyProduct(subscription.Product))
            .Select(ToDetails)
            .OrderBy(subscription => subscription.PlanName)
            .ToList();
    }

    private async Task<SubscriptionDetails> SubscribeUnderLockAsync(ApplicationUser user, SubscriptionPlan plan,
        CancellationToken cancellationToken)
    {
        var customerReference = CustomerReference(user);
        var customer = await EnsureCustomerAsync(user, customerReference, cancellationToken);
        var subscriptionReference = SubscriptionReference(user, plan.Handle);
        var mapping = await GetOrClaimMappingAsync(user.Id, plan.Handle, customer.Id, customerReference,
            subscriptionReference, cancellationToken);

        if (mapping.MaxioSubscriptionId is int mappedSubscriptionId)
        {
            var existing = (await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken))
                .FirstOrDefault(subscription => subscription.Id == mappedSubscriptionId ||
                    string.Equals(subscription.Reference, subscriptionReference, StringComparison.Ordinal));
            if (existing is not null)
                return ToDetails(existing);
        }

        var customerSubscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var alreadyCreated = customerSubscriptions.FirstOrDefault(subscription =>
            string.Equals(subscription.Reference, subscriptionReference, StringComparison.Ordinal));
        if (alreadyCreated is not null)
            return await CompleteMappingAsync(mapping, alreadyCreated, cancellationToken);

        try
        {
            var created = await _maxio.CreateSubscriptionAsync(new MaxioSubscriptionRequest
            {
                ProductHandle = plan.Handle,
                CustomerId = customer.Id,
                Reference = subscriptionReference,
                // Remittance is a spec-defined collection method that does not require
                // card capture for no-payment-method-required plans.
                PaymentCollectionMethod = "remittance"
            }, cancellationToken);
            return await CompleteMappingAsync(mapping, created, cancellationToken);
        }
        catch (MaxioApiException exception) when (exception.StatusCode == 422)
        {
            // A retry after a network timeout or a concurrent request may have succeeded.
            var retryMatch = (await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken))
                .FirstOrDefault(subscription => string.Equals(subscription.Reference, subscriptionReference, StringComparison.Ordinal));
            if (retryMatch is not null)
                return await CompleteMappingAsync(mapping, retryMatch, cancellationToken);
            throw;
        }
        catch
        {
            _identityDb.MaxioSubscriptionMappings.Remove(mapping);
            await _identityDb.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(ApplicationUser user, string reference,
        CancellationToken cancellationToken)
    {
        var existing = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
            return existing;

        var (firstName, lastName) = CustomerName(user);
        try
        {
            return await _maxio.CreateCustomerAsync(new MaxioCustomerRequest
            {
                FirstName = firstName,
                LastName = lastName,
                Email = user.Email!,
                Reference = reference
            }, cancellationToken);
        }
        catch (MaxioApiException exception) when (exception.StatusCode == 422)
        {
            // Customer reference is the Maxio uniqueness boundary. Resolve a create race.
            var racedCustomer = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
            if (racedCustomer is not null)
                return racedCustomer;
            throw;
        }
    }

    private async Task<MaxioSubscriptionMapping> GetOrClaimMappingAsync(string userId, string planHandle,
        int customerId, string customerReference, string subscriptionReference, CancellationToken cancellationToken)
    {
        var mapping = await _identityDb.MaxioSubscriptionMappings
            .FirstOrDefaultAsync(item => item.UserId == userId && item.PlanHandle == planHandle, cancellationToken);
        if (mapping is not null)
        {
            if (mapping.MaxioSubscriptionId is not null)
                return mapping;

            var age = DateTime.UtcNow - (mapping.CreationStartedAtUtc ?? DateTime.MinValue);
            if (age < TimeSpan.FromSeconds(30))
                throw new SubscriptionCreationInProgressException();

            mapping.CreationToken = Guid.NewGuid().ToString("N");
            mapping.CreationStartedAtUtc = DateTime.UtcNow;
            mapping.MaxioCustomerId = customerId;
            mapping.UpdatedAtUtc = DateTime.UtcNow;
            await _identityDb.SaveChangesAsync(cancellationToken);
            return mapping;
        }

        mapping = new MaxioSubscriptionMapping
        {
            UserId = userId,
            PlanHandle = planHandle,
            CustomerReference = customerReference,
            MaxioCustomerId = customerId,
            SubscriptionReference = subscriptionReference,
            CreationToken = Guid.NewGuid().ToString("N"),
            CreationStartedAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        _identityDb.MaxioSubscriptionMappings.Add(mapping);
        try
        {
            await _identityDb.SaveChangesAsync(cancellationToken);
            return mapping;
        }
        catch (DbUpdateException)
        {
            _identityDb.Entry(mapping).State = EntityState.Detached;
            var winner = await _identityDb.MaxioSubscriptionMappings
                .FirstAsync(item => item.UserId == userId && item.PlanHandle == planHandle, cancellationToken);
            if (winner.MaxioSubscriptionId is null)
                throw new SubscriptionCreationInProgressException();
            return winner;
        }
    }

    private async Task<SubscriptionDetails> CompleteMappingAsync(MaxioSubscriptionMapping mapping,
        MaxioSubscription subscription, CancellationToken cancellationToken)
    {
        mapping.MaxioSubscriptionId = subscription.Id;
        mapping.CreationToken = null;
        mapping.CreationStartedAtUtc = null;
        mapping.UpdatedAtUtc = DateTime.UtcNow;
        await _identityDb.SaveChangesAsync(cancellationToken);
        return ToDetails(subscription);
    }

    private bool IsConfiguredFamilyProduct(MaxioProduct product) =>
        string.Equals(product.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase);

    private static SubscriptionPlan ToPlan(MaxioProduct product) => new(
        product.Handle!, product.Name ?? product.Handle!, product.Description,
        product.PriceInCents, product.Interval, product.IntervalUnit ?? string.Empty);

    private static SubscriptionDetails ToDetails(MaxioSubscription subscription) => new(
        subscription.Id,
        subscription.Product?.Handle,
        subscription.Product?.Name,
        subscription.PriceInCents != 0 ? subscription.PriceInCents : subscription.Product?.PriceInCents ?? 0,
        subscription.State,
        subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt);

    private static string CustomerReference(ApplicationUser user) => $"eshop-user-{user.Id}";
    private static string SubscriptionReference(ApplicationUser user, string planHandle) =>
        $"eshop-subscription-{user.Id}-{planHandle}";

    private static (string FirstName, string LastName) CustomerName(ApplicationUser user)
    {
        var localPart = (user.Email ?? user.UserName ?? "eShop Customer").Split('@')[0];
        var words = localPart.Split(new[] { '.', '-', '_', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        return words.Length >= 2
            ? (words[0], string.Join(" ", words.Skip(1)))
            : (words.FirstOrDefault() ?? "eShop", "Customer");
    }
}

public sealed class SubscriptionRequestException : Exception
{
    public SubscriptionRequestException(string message) : base(message) { }
}

public sealed class SubscriptionCreationInProgressException : Exception
{
    public SubscriptionCreationInProgressException() : base("A subscription request for this plan is already being processed.") { }
}
