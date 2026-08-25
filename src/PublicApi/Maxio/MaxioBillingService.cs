using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class MaxioBillingService : IMaxioBillingService
{
    // States in which a subscription already entitles the customer; used to make
    // Subscribe idempotent instead of creating a duplicate subscription.
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "trialing", "assessing", "pending", "past_due", "on_hold", "awaiting_signup"
    };

    private readonly IMaxioApiClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioBillingService> _logger;

    // Serializes subscribe calls per user so a double-click (or concurrent retries)
    // cannot race past the existing-subscription check.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _subscribeLocks = new();

    public MaxioBillingService(IMaxioApiClient client, IOptions<MaxioSettings> settings, ILogger<MaxioBillingService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanModel>> GetSubscriptionPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _client.ListProductsAsync(_settings.ProductFamilyHandle, cancellationToken);

        return products
            .Where(p => p.ArchivedAt == null && !string.IsNullOrEmpty(p.Handle))
            .OrderBy(p => p.PriceInCents)
            .Select(p => new SubscriptionPlanModel(
                p.Name ?? p.Handle!,
                p.Handle!,
                p.Description,
                p.PriceInCents,
                p.Interval,
                p.IntervalUnit ?? "month",
                p.RequireCreditCard))
            .ToList();
    }

    public async Task<SubscribeResultModel> SubscribeAsync(string username, string productHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new ArgumentException("A product handle is required.", nameof(productHandle));
        }

        var product = await FindProductAsync(productHandle, cancellationToken);
        if (product == null)
        {
            throw new ArgumentException(
                $"No plan with handle '{productHandle}' exists in the configured product family.", nameof(productHandle));
        }

        var customer = await EnsureCustomerAsync(username, cancellationToken);
        var subscriptionReference = $"{username}:{productHandle}";

        var userLock = _subscribeLocks.GetOrAdd(username, _ => new SemaphoreSlim(1, 1));
        await userLock.WaitAsync(cancellationToken);
        try
        {
            var existing = await FindLiveSubscriptionAsync(customer.Id, productHandle, cancellationToken);
            if (existing != null)
            {
                _logger.LogInformation("User {Username} already has a live subscription {SubscriptionId} for {ProductHandle}; returning it.",
                    username, existing.Id, productHandle);
                return new SubscribeResultModel(ToModel(existing), AlreadyExisted: true);
            }

            try
            {
                var created = await _client.CreateSubscriptionAsync(new MaxioSubscriptionRequestItem
                {
                    ProductHandle = productHandle,
                    CustomerReference = username,
                    Reference = subscriptionReference,
                    // Plans that don't require a payment method are billed by invoice
                    // (remittance); otherwise Maxio would reject the signup for lack of
                    // a card covering the signup balance.
                    PaymentCollectionMethod = product.RequireCreditCard ? null : "remittance"
                }, cancellationToken);

                _logger.LogInformation("Created subscription {SubscriptionId} for user {Username} on plan {ProductHandle}.",
                    created.Id, username, productHandle);
                return new SubscribeResultModel(ToModel(created), AlreadyExisted: false);
            }
            catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                // A concurrent request (e.g. a retry that bypassed this process) may have
                // created the subscription first; the reference makes it recoverable.
                var duplicate = await _client.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
                if (duplicate != null)
                {
                    return new SubscribeResultModel(ToModel(duplicate), AlreadyExisted: true);
                }

                throw;
            }
        }
        finally
        {
            userLock.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionModel>> GetMySubscriptionsAsync(string username, CancellationToken cancellationToken = default)
    {
        var customer = await _client.FindCustomerByReferenceAsync(username, cancellationToken);
        if (customer == null)
        {
            return Array.Empty<SubscriptionModel>();
        }

        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(ToModel).ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(string username, CancellationToken cancellationToken)
    {
        var existing = await _client.FindCustomerByReferenceAsync(username, cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        var (firstName, lastName) = DeriveName(username);
        try
        {
            var created = await _client.CreateCustomerAsync(new MaxioCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = username,
                Reference = username
            }, cancellationToken);

            _logger.LogInformation("Created Maxio customer {CustomerId} for user {Username}.", created.Id, username);
            return created;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Reference uniqueness is enforced by Maxio; a conflict means another
            // request created the customer first, so look it up again.
            var raced = await _client.FindCustomerByReferenceAsync(username, cancellationToken);
            if (raced != null)
            {
                return raced;
            }

            throw;
        }
    }

    private async Task<MaxioProduct?> FindProductAsync(string productHandle, CancellationToken cancellationToken)
    {
        var products = await _client.ListProductsAsync(_settings.ProductFamilyHandle, cancellationToken);
        return products.FirstOrDefault(p =>
            string.Equals(p.Handle, productHandle, StringComparison.OrdinalIgnoreCase) && p.ArchivedAt == null);
    }

    private async Task<MaxioSubscription?> FindLiveSubscriptionAsync(int customerId, string productHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase) &&
            s.State != null && LiveStates.Contains(s.State));
    }

    private static SubscriptionModel ToModel(MaxioSubscription subscription)
    {
        return new SubscriptionModel(
            subscription.Id,
            subscription.State ?? "unknown",
            subscription.Product?.Name ?? subscription.Product?.Handle ?? string.Empty,
            subscription.Product?.Handle ?? string.Empty,
            subscription.ProductPriceInCents,
            subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt,
            subscription.ActivatedAt);
    }

    private static (string FirstName, string LastName) DeriveName(string username)
    {
        var localPart = username.Split('@')[0];
        var parts = localPart.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2
            ? (parts[0], parts[1])
            : (localPart, "Customer");
    }
}
