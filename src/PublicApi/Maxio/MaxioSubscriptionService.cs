using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public sealed record SubscriptionPlanDto(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit);

public sealed record SubscriptionDto(
    int Id,
    string PlanHandle,
    string PlanName,
    long PriceInCents,
    string State,
    DateTimeOffset? NextBillingDate);

public sealed class MaxioSubscriptionService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserLocks = new();
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create", "trial_ended"
    };

    private readonly IMaxioBillingClient _client;
    private readonly AppIdentityDbContext _identityDb;
    private readonly MaxioSettings _settings;

    public MaxioSubscriptionService(
        IMaxioBillingClient client,
        AppIdentityDbContext identityDb,
        Microsoft.Extensions.Options.IOptions<MaxioSettings> settings)
    {
        _client = client;
        _identityDb = identityDb;
        _settings = settings.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var products = await _client.ListProductsAsync(_settings.ProductFamilyHandle!, cancellationToken);
        return products
            .Where(product => product.ArchivedAt is null && !string.IsNullOrWhiteSpace(product.Handle))
            .Select(product => new SubscriptionPlanDto(
                product.Handle!,
                product.Name,
                product.Description,
                product.PriceInCents,
                product.Interval,
                product.IntervalUnit))
            .ToArray();
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(
        ApplicationUser user,
        CancellationToken cancellationToken)
    {
        var customerReference = CustomerReference(user.Id);
        var customer = await _client.FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        await SaveMappingsAsync(user.Id, customer.Id, subscriptions, cancellationToken);
        return subscriptions.Select(ToDto).ToArray();
    }

    public async Task<SubscriptionDto> SubscribeAsync(
        ApplicationUser user,
        string planHandle,
        CancellationToken cancellationToken)
    {
        var plans = await ListPlansAsync(cancellationToken);
        var plan = plans.SingleOrDefault(item => string.Equals(item.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new SubscriptionPlanNotFoundException(planHandle);
        }

        var lockKey = $"{user.Id}:{plan.Handle.ToUpperInvariant()}";
        var userLock = UserLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await userLock.WaitAsync(cancellationToken);
        try
        {
            var existingMapping = await _identityDb.MaxioSubscriptionMappings
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    mapping => mapping.UserId == user.Id && mapping.ProductHandle == plan.Handle,
                    cancellationToken);
            if (existingMapping is not null)
            {
                var remoteSubscriptions = await _client.ListCustomerSubscriptionsAsync(
                    existingMapping.MaxioCustomerId,
                    cancellationToken);
                var mappedSubscription = remoteSubscriptions.FirstOrDefault(
                    subscription => subscription.Id == existingMapping.MaxioSubscriptionId);
                if (mappedSubscription is not null && !TerminalStates.Contains(mappedSubscription.State))
                {
                    return ToDto(mappedSubscription);
                }
            }

            var customer = await EnsureCustomerAsync(user, cancellationToken);
            var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var existingSubscription = subscriptions.FirstOrDefault(subscription =>
                !TerminalStates.Contains(subscription.State) &&
                (string.Equals(subscription.Reference, SubscriptionReference(user.Id, plan.Handle), StringComparison.Ordinal) ||
                 string.Equals(subscription.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase)));

            var subscription = existingSubscription ?? await CreateSubscriptionAsync(
                user,
                customer.Id,
                plan.Handle,
                subscriptions,
                cancellationToken);

            await SaveMappingAsync(user.Id, customer.Id, plan.Handle, subscription, cancellationToken);
            return ToDto(subscription);
        }
        finally
        {
            userLock.Release();
        }
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(
        ApplicationUser user,
        CancellationToken cancellationToken)
    {
        var reference = CustomerReference(user.Id);
        var existingCustomer = await _client.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existingCustomer is not null)
        {
            return existingCustomer;
        }

        var email = user.Email ?? user.UserName ?? $"{user.Id}@invalid.local";
        var localPart = email.Split('@', 2)[0];
        try
        {
            return await _client.CreateCustomerAsync(new MaxioCreateCustomer
            {
                FirstName = string.IsNullOrWhiteSpace(localPart) ? "eShop" : localPart,
                LastName = "Shopper",
                Email = email,
                Reference = reference,
                UniquenessToken = StableUniquenessToken("customer", reference)
            }, cancellationToken);
        }
        catch (MaxioApiException ex) when (
            ex.StatusCode == System.Net.HttpStatusCode.Conflict ||
            ex.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
        {
            // The reference is unique in Maxio. A concurrent request may have won
            // the create race, so resolve the customer before surfacing the error.
            var concurrentCustomer = await _client.FindCustomerByReferenceAsync(reference, cancellationToken);
            if (concurrentCustomer is not null)
            {
                return concurrentCustomer;
            }

            throw;
        }
    }

    private async Task<MaxioSubscription> CreateSubscriptionAsync(
        ApplicationUser user,
        int customerId,
        string planHandle,
        IReadOnlyList<MaxioSubscription> existingSubscriptions,
        CancellationToken cancellationToken)
    {
        var reference = SubscriptionReference(user.Id, planHandle);
        try
        {
            return await _client.CreateSubscriptionAsync(new MaxioCreateSubscription
            {
                CustomerId = customerId,
                ProductHandle = planHandle,
                Reference = reference,
                PaymentCollectionMethod = "remittance",
                UniquenessToken = StableUniquenessToken("subscription", reference)
            }, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            // A uniqueness-token conflict means Maxio received an earlier request.
            // Recover the resulting subscription by its stable reference.
            var recovered = existingSubscriptions.FirstOrDefault(subscription =>
                string.Equals(subscription.Reference, reference, StringComparison.Ordinal));
            if (recovered is not null)
            {
                return recovered;
            }

            var refreshed = await _client.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
            recovered = refreshed.FirstOrDefault(subscription =>
                string.Equals(subscription.Reference, reference, StringComparison.Ordinal));
            if (recovered is not null)
            {
                return recovered;
            }

            throw;
        }
    }

    private async Task SaveMappingsAsync(
        string userId,
        int customerId,
        IReadOnlyList<MaxioSubscription> subscriptions,
        CancellationToken cancellationToken)
    {
        foreach (var subscription in subscriptions.Where(item => item.Product?.Handle is not null))
        {
            await SaveMappingAsync(userId, customerId, subscription.Product!.Handle!, subscription, cancellationToken);
        }
    }

    private async Task SaveMappingAsync(
        string userId,
        int customerId,
        string planHandle,
        MaxioSubscription subscription,
        CancellationToken cancellationToken)
    {
        var mapping = await _identityDb.MaxioSubscriptionMappings.SingleOrDefaultAsync(
            item => item.UserId == userId && item.ProductHandle == planHandle,
            cancellationToken);
        if (mapping is null)
        {
            _identityDb.MaxioSubscriptionMappings.Add(new MaxioSubscriptionMapping
            {
                UserId = userId,
                MaxioCustomerId = customerId,
                MaxioSubscriptionId = subscription.Id,
                ProductHandle = planHandle,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            mapping.MaxioCustomerId = customerId;
            mapping.MaxioSubscriptionId = subscription.Id;
        }

        await _identityDb.SaveChangesAsync(cancellationToken);
    }

    private static SubscriptionDto ToDto(MaxioSubscription subscription) => new(
        subscription.Id,
        subscription.Product?.Handle ?? string.Empty,
        subscription.Product?.Name ?? string.Empty,
        subscription.PriceInCents,
        subscription.State,
        subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt);

    private static string CustomerReference(string userId) => $"eshop-user:{userId}";
    private static string SubscriptionReference(string userId, string planHandle) => $"eshop-subscription:{userId}:{planHandle}";

    private static string StableUniquenessToken(string operation, string reference)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"eshoponweb:{operation}:{reference}"));
        return Convert.ToHexString(bytes);
    }
}

public sealed class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string planHandle)
        : base($"Subscription plan '{planHandle}' was not found in the configured Maxio product family.")
    {
    }
}
