using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class SubscriptionBillingService : ISubscriptionBillingService
{
    // Maxio subscription states that are not end-of-life; an existing subscription in one
    // of these states means the shopper is already enrolled and must not be re-enrolled.
    private static readonly HashSet<string> EndOfLifeStates = new()
    {
        "canceled", "expired", "failed_to_create", "on_hold", "suspended", "trial_ended"
    };

    // Serializes subscribe attempts per user so a double-click cannot race past the
    // existing-subscription check and create two customers/subscriptions.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserLocks = new();

    private readonly IMaxioClient _maxioClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<SubscriptionBillingService> _logger;

    public SubscriptionBillingService(IMaxioClient maxioClient, IOptions<MaxioSettings> settings, ILogger<SubscriptionBillingService> logger)
    {
        _maxioClient = maxioClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        return await _maxioClient.ListProductsAsync(_settings.ProductFamilyHandle, cancellationToken);
    }

    public async Task<SubscribeResult> SubscribeAsync(string userId, string username, string email, string productHandle, CancellationToken cancellationToken = default)
    {
        var userLock = UserLocks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await userLock.WaitAsync(cancellationToken);
        try
        {
            var customer = await GetOrCreateCustomerAsync(userId, username, email, cancellationToken);

            var existing = (await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken))
                .FirstOrDefault(s => s.Product?.Handle == productHandle
                    && (s.State is null || !EndOfLifeStates.Contains(s.State)));

            if (existing is not null)
            {
                _logger.LogInformation("User {UserId} already has a live subscription {SubscriptionId} to {ProductHandle}; returning it.",
                    userId, existing.Id, productHandle);
                return new SubscribeResult { Subscription = existing, AlreadySubscribed = true };
            }

            var subscription = await _maxioClient.CreateSubscriptionAsync(productHandle, customer.Reference ?? userId, cancellationToken);
            _logger.LogInformation("Created subscription {SubscriptionId} for user {UserId} on plan {ProductHandle}.",
                subscription.Id, userId, productHandle);
            return new SubscribeResult { Subscription = subscription, AlreadySubscribed = false };
        }
        finally
        {
            userLock.Release();
        }
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsForUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        var customer = await _maxioClient.FindCustomerByReferenceAsync(userId, cancellationToken);
        if (customer is null)
        {
            return new List<MaxioSubscription>();
        }

        return await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    private async Task<MaxioCustomer> GetOrCreateCustomerAsync(string userId, string username, string email, CancellationToken cancellationToken)
    {
        var existing = await _maxioClient.FindCustomerByReferenceAsync(userId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            return await _maxioClient.CreateCustomerAsync(username, "eShopOnWeb", email, userId, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Lost a race with a concurrent create on Maxio's side (reference is unique per site);
            // the customer now exists, so look it up.
            _logger.LogWarning("Customer create for reference {Reference} returned 422; re-reading existing customer.", userId);
            var customer = await _maxioClient.FindCustomerByReferenceAsync(userId, cancellationToken);
            if (customer is not null)
            {
                return customer;
            }

            throw;
        }
    }
}
