using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Orchestrates the subscription billing flows against Maxio Advanced Billing.
/// All operations are idempotent: the eShopOnWeb user id is used as the Maxio
/// customer reference, and a deterministic subscription reference
/// ("{userId}:{productHandle}") guarantees a double-click never creates two
/// customers or two subscriptions for the same plan.
/// </summary>
public class MaxioSubscriptionService
{
    private readonly IMaxioClient _maxioClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(IMaxioClient maxioClient,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioSubscriptionService> logger)
    {
        _maxioClient = maxioClient;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <summary>Lists the purchasable (non-archived) plans in the configured product family.</summary>
    public async Task<IReadOnlyList<MaxioProduct>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        _settings.Validate();

        var products = await _maxioClient.ListProductsAsync(_settings.ProductFamilyHandle, cancellationToken);
        return products.Where(p => p.ArchivedAt is null).ToList();
    }

    /// <summary>
    /// Subscribes the given user to a plan. Returns the existing subscription when this
    /// user is already subscribed to the same plan, so retries are safe.
    /// </summary>
    public async Task<MaxioSubscription> SubscribeAsync(string userId, string email, string productHandle,
        CancellationToken cancellationToken = default)
    {
        _settings.Validate();

        var product = await _maxioClient.GetProductByHandleAsync(productHandle, cancellationToken);
        if (product is null || product.ArchivedAt is not null)
        {
            throw new MaxioApiException(HttpStatusCode.NotFound,
                $"No active plan with handle '{productHandle}' exists in Maxio.");
        }

        var customer = await EnsureCustomerAsync(userId, email, cancellationToken);

        var subscriptionReference = BuildSubscriptionReference(userId, productHandle);
        var existing = await _maxioClient.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation("Subscription {Reference} already exists (id {SubscriptionId}); returning it.",
                subscriptionReference, existing.Id);
            return existing;
        }

        try
        {
            return await _maxioClient.CreateSubscriptionAsync(new MaxioCreateSubscriptionRequest
            {
                ProductHandle = productHandle,
                CustomerReference = customer.Reference,
                Reference = subscriptionReference,
                // The seeded plans require no payment method; remittance billing issues an
                // invoice at renewal instead of attempting an automatic card charge.
                PaymentCollectionMethod = "remittance"
            }, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Lost a race with a concurrent request — the subscription now exists; fetch and return it.
            var concurrent = await _maxioClient.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
            if (concurrent is not null)
            {
                return concurrent;
            }
            throw;
        }
    }

    /// <summary>Lists the user's subscriptions; empty when the user has no Maxio customer record yet.</summary>
    public async Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsAsync(string userId, CancellationToken cancellationToken = default)
    {
        _settings.Validate();

        var customer = await _maxioClient.FindCustomerByReferenceAsync(userId, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<MaxioSubscription>();
        }

        return await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(string userId, string email, CancellationToken cancellationToken)
    {
        var existing = await _maxioClient.FindCustomerByReferenceAsync(userId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            var (firstName, lastName) = DeriveNames(email);
            return await _maxioClient.CreateCustomerAsync(new MaxioCreateCustomerRequest
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = userId
            }, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Maxio enforces one customer per reference; a concurrent request created it first.
            var concurrent = await _maxioClient.FindCustomerByReferenceAsync(userId, cancellationToken);
            if (concurrent is not null)
            {
                return concurrent;
            }
            throw;
        }
    }

    private static string BuildSubscriptionReference(string userId, string productHandle) => $"{userId}:{productHandle}";

    private static (string FirstName, string LastName) DeriveNames(string email)
    {
        var localPart = email.Split('@')[0];
        var segments = localPart.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        return segments.Length >= 2
            ? (Capitalize(segments[0]), Capitalize(segments[1]))
            : (Capitalize(localPart), "Customer");
    }

    private static string Capitalize(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value[1..];
}
