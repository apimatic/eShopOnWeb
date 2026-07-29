using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Default <see cref="ISubscriptionService"/>. Coordinates the Maxio client, the per-user idempotency
/// guard, and the configured product family to deliver idempotent subscribe/enrollment semantics.
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    // Subscription states that mean "there is no live enrollment" — a new subscription may be created.
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create", "trial_ended"
    };

    // The eShopOnWeb plans require no payment method, so subscriptions are created with invoice
    // (remittance) collection rather than automatic card capture. With automatic collection Maxio would
    // attempt to charge the plan price at signup and reject the subscription for having no card on file.
    private const string CardlessPaymentCollectionMethod = "remittance";

    private readonly IMaxioClient _client;
    private readonly MaxioIdempotencyGuard _guard;
    private readonly MaxioSettings _settings;
    private readonly IAppLogger<SubscriptionService> _logger;

    public SubscriptionService(
        IMaxioClient client,
        MaxioIdempotencyGuard guard,
        IOptions<MaxioSettings> settings,
        IAppLogger<SubscriptionService> logger)
    {
        _client = client;
        _guard = guard;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MaxioProduct>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _client.ListProductsForFamilyAsync(_settings.ProductFamilyHandle, cancellationToken);
        return products.Where(p => p.ArchivedAt == null).ToList();
    }

    public Task<SubscribeResult> SubscribeAsync(BillingUser user, string planHandle, CancellationToken cancellationToken = default)
    {
        // Serialize per user so a double-click cannot create two customers/subscriptions.
        return _guard.RunExclusiveAsync(user.Reference, async () =>
        {
            var customer = await EnsureCustomerAsync(user, cancellationToken);

            // Idempotency: if the user already has a live subscription to this plan, return it unchanged.
            var existing = await FindLiveSubscriptionAsync(customer.Id, planHandle, cancellationToken);
            if (existing != null)
            {
                _logger.LogInformation($"User {user.Reference} already subscribed to '{planHandle}' (subscription {existing.Id}); returning existing.");
                return new SubscribeResult(existing, AlreadyExisted: true);
            }

            var attributes = new MaxioSubscriptionAttributes
            {
                CustomerId = customer.Id,
                ProductHandle = planHandle,
                PaymentCollectionMethod = CardlessPaymentCollectionMethod
            };
            var uniquenessToken = Guid.NewGuid().ToString("N");

            try
            {
                var created = await _client.CreateSubscriptionAsync(attributes, uniquenessToken, cancellationToken);
                _logger.LogInformation($"Created subscription {created.Id} for user {user.Reference} on plan '{planHandle}'.");
                return new SubscribeResult(created, AlreadyExisted: false);
            }
            catch (MaxioApiException ex) when (ex.IsDuplicate)
            {
                // A retried create raced a successful original: reconcile by re-reading.
                var reconciled = await FindLiveSubscriptionAsync(customer.Id, planHandle, cancellationToken);
                if (reconciled != null)
                {
                    return new SubscribeResult(reconciled, AlreadyExisted: true);
                }
                throw;
            }
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<MaxioSubscription>> GetSubscriptionsAsync(BillingUser user, CancellationToken cancellationToken = default)
    {
        var customer = await _client.FindCustomerByReferenceAsync(user.Reference, cancellationToken);
        if (customer == null)
        {
            return Array.Empty<MaxioSubscription>();
        }
        return await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    /// <summary>
    /// Returns the existing Maxio customer for the user, creating one if necessary. Idempotent on the
    /// unique customer <c>reference</c>: a create that loses a race (422) falls back to a re-lookup.
    /// </summary>
    private async Task<MaxioCustomer> EnsureCustomerAsync(BillingUser user, CancellationToken cancellationToken)
    {
        var existing = await _client.FindCustomerByReferenceAsync(user.Reference, cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        var (firstName, lastName) = DeriveName(user.Email, user.Reference);
        var attributes = new MaxioCustomerAttributes
        {
            Reference = user.Reference,
            Email = user.Email,
            FirstName = firstName,
            LastName = lastName,
            Organization = "eShopOnWeb"
        };

        try
        {
            var created = await _client.CreateCustomerAsync(attributes, cancellationToken);
            _logger.LogInformation($"Created Maxio customer {created.Id} for user {user.Reference}.");
            return created;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // The reference may have just been taken (e.g. a concurrent request in another process).
            var afterConflict = await _client.FindCustomerByReferenceAsync(user.Reference, cancellationToken);
            if (afterConflict != null)
            {
                return afterConflict;
            }
            throw;
        }
    }

    private async Task<MaxioSubscription?> FindLiveSubscriptionAsync(int customerId, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase) &&
            !IsTerminal(s.State));
    }

    private static bool IsTerminal(string? state)
        => state != null && TerminalStates.Contains(state);

    /// <summary>
    /// Derives a first/last name for the Maxio customer record. eShopOnWeb users carry only an email, so we
    /// use the email local-part as the first name and a stable brand suffix as the last name (both are
    /// required by Maxio when creating a customer via attributes).
    /// </summary>
    private static (string FirstName, string LastName) DeriveName(string email, string reference)
    {
        var localPart = email;
        var atIndex = email.IndexOf('@');
        if (atIndex > 0)
        {
            localPart = email.Substring(0, atIndex);
        }

        var firstName = string.IsNullOrWhiteSpace(localPart) ? reference : localPart;
        return (firstName, "eShopOnWeb");
    }
}
