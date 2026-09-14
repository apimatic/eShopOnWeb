using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Subscription billing backed by Maxio Advanced Billing.
/// </summary>
/// <remarks>
/// <para>Idempotency is layered, because each layer covers what the one before it cannot:</para>
/// <list type="number">
///   <item>a per-account lock serialises concurrent requests inside this process;</item>
///   <item>the shopper's billing customer is keyed on a stable, namespaced reference, so it is looked
///   up before it is created, and a lost race surfaces as "reference must be unique" and is
///   reconciled;</item>
///   <item>an existing live subscription to the same plan short circuits the signup entirely;</item>
///   <item>the create call carries a deterministic subscription reference, which is unique per site,
///   so a duplicate that slips past the checks above is refused by the server and reconciled against
///   the record that already exists - and a uniqueness token, which does the same for a request the
///   transport replayed after a timeout.</item>
/// </list>
/// <para>
/// Nothing about the mapping is stored locally: the billing system is the system of record, and the
/// shopper's account key is the only join between the two. That keeps the integration correct across
/// restarts and across instances.
/// </para>
/// </remarks>
public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    /// <summary>
    /// Namespace for every reference this integration writes, so records it owns are recognisable and
    /// cannot collide with references written to the same site by another system.
    /// </summary>
    private const string ReferenceNamespace = "eshoponweb";

    private readonly IMaxioApiClient _client;
    private readonly MaxioSiteCache _siteCache;
    private readonly KeyedAsyncLock _accountLocks;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        IMaxioApiClient client,
        MaxioSiteCache siteCache,
        KeyedAsyncLock accountLocks,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _siteCache = siteCache;
        _accountLocks = accountLocks;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await ExecuteAsync(
            () => _client.ListProductsForFamilyAsync(_settings.ProductFamilyHandle, cancellationToken),
            "list subscription plans", cancellationToken);

        return products
            .Where(product => product.ArchivedAt is null && !string.IsNullOrWhiteSpace(product.Handle))
            .Select(ToPlan)
            .OrderBy(plan => plan.PriceInCents)
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscriberAccount account, string planHandle, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new ArgumentException("A plan handle is required.", nameof(planHandle));
        }

        var plans = await ListPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(candidate => string.Equals(candidate.Handle, planHandle, StringComparison.OrdinalIgnoreCase))
            ?? throw new SubscriptionPlanNotFoundException(planHandle, _settings.ProductFamilyHandle);

        if (plan.RequiresPaymentMethod)
        {
            // Failing here keeps the shopper from meeting a confusing gateway error mid-signup: this
            // integration captures no card, so a plan that demands one can never be subscribed to.
            throw new SubscriptionBillingValidationException(new[]
            {
                $"Plan '{plan.Handle}' requires a payment method on file, which this integration does not capture."
            });
        }

        var customerReference = BuildCustomerReference(account);

        using (await _accountLocks.AcquireAsync(customerReference, cancellationToken))
        {
            var customer = await EnsureCustomerAsync(account, customerReference, cancellationToken);
            var subscriptions = await ExecuteAsync(
                () => _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken),
                "list customer subscriptions", cancellationToken);

            var existing = FindLiveSubscriptionForPlan(subscriptions, plan.Handle);
            if (existing is not null)
            {
                _logger.LogInformation("Maxio customer {CustomerId} is already subscribed to plan {PlanHandle} (subscription {SubscriptionId}); returning the existing subscription.",
                    customer.Id, plan.Handle, existing.Id);

                return new SubscribeResult(ToSubscription(existing), alreadyExisted: true);
            }

            var subscriptionReference = BuildSubscriptionReference(account, plan.Handle, CountSubscriptionsForPlan(subscriptions, plan.Handle));
            var request = new MaxioCreateSubscriptionRequest
            {
                Subscription = new MaxioSubscriptionAttributes
                {
                    ProductHandle = plan.Handle,
                    CustomerId = customer.Id,
                    Reference = subscriptionReference,
                    PaymentCollectionMethod = await ResolvePaymentCollectionMethodAsync(cancellationToken)
                },
                UniquenessToken = BuildUniquenessToken()
            };

            try
            {
                var created = await ExecuteAsync(
                    () => _client.CreateSubscriptionAsync(request, cancellationToken),
                    "create subscription", cancellationToken);

                _logger.LogInformation("Created Maxio subscription {SubscriptionId} for customer {CustomerId} on plan {PlanHandle}.",
                    created.Id, customer.Id, plan.Handle);

                return new SubscribeResult(ToSubscription(created), alreadyExisted: false);
            }
            catch (MaxioDuplicateSubmissionException)
            {
                // An identical create was already accepted inside the server's de-duplication window.
                return await ReconcileDuplicateAsync(customer.Id, plan.Handle, subscriptionReference, cancellationToken);
            }
            catch (MaxioValidationException ex) when (ex.IsDuplicateReference)
            {
                // Another caller won the race and already used this subscription reference.
                return await ReconcileDuplicateAsync(customer.Id, plan.Handle, subscriptionReference, cancellationToken);
            }
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(SubscriberAccount account, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);

        var customerReference = BuildCustomerReference(account);
        var customer = await ExecuteAsync(
            () => _client.FindCustomerByReferenceAsync(customerReference, cancellationToken),
            "look up billing customer", cancellationToken);

        if (customer is null)
        {
            // The shopper has never subscribed, which is an empty list rather than an error.
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await ExecuteAsync(
            () => _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken),
            "list customer subscriptions", cancellationToken);

        return subscriptions
            .Select(ToSubscription)
            .OrderByDescending(subscription => subscription.CreatedAt ?? DateTimeOffset.MinValue)
            .ToList();
    }

    /// <summary>
    /// Looks the billing customer up by reference and creates it only when it is genuinely absent.
    /// A create that loses a race is refused by the server, and the winner's record is used instead.
    /// </summary>
    private async Task<MaxioCustomer> EnsureCustomerAsync(SubscriberAccount account, string customerReference, CancellationToken cancellationToken)
    {
        var existing = await ExecuteAsync(
            () => _client.FindCustomerByReferenceAsync(customerReference, cancellationToken),
            "look up billing customer", cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = SplitName(account);
        var attributes = new MaxioCustomerAttributes
        {
            FirstName = firstName,
            LastName = lastName,
            Email = account.Email,
            Reference = customerReference
        };

        try
        {
            var created = await ExecuteAsync(
                () => _client.CreateCustomerAsync(attributes, cancellationToken),
                "create billing customer", cancellationToken);

            _logger.LogInformation("Created Maxio customer {CustomerId} for reference {CustomerReference}.", created.Id, customerReference);
            return created;
        }
        catch (MaxioValidationException ex) when (ex.IsDuplicateReference)
        {
            var winner = await ExecuteAsync(
                () => _client.FindCustomerByReferenceAsync(customerReference, cancellationToken),
                "look up billing customer", cancellationToken);

            if (winner is not null)
            {
                _logger.LogInformation("Maxio customer for reference {CustomerReference} was created concurrently; using customer {CustomerId}.",
                    customerReference, winner.Id);

                return winner;
            }

            throw new SubscriptionBillingException(
                $"The billing system reported that customer reference '{customerReference}' is taken, but no customer could be read back.", ex);
        }
    }

    /// <summary>
    /// Resolves a create that the server refused as a duplicate back to the subscription that exists.
    /// </summary>
    private async Task<SubscribeResult> ReconcileDuplicateAsync(long customerId, string planHandle, string subscriptionReference, CancellationToken cancellationToken)
    {
        var subscriptions = await ExecuteAsync(
            () => _client.ListCustomerSubscriptionsAsync(customerId, cancellationToken),
            "list customer subscriptions", cancellationToken);

        var match = subscriptions.FirstOrDefault(subscription =>
                        string.Equals(subscription.Reference, subscriptionReference, StringComparison.Ordinal))
                    ?? FindLiveSubscriptionForPlan(subscriptions, planHandle);

        if (match is null)
        {
            throw new SubscriptionBillingException(
                $"The billing system rejected the signup for plan '{planHandle}' as a duplicate, but no matching subscription could be read back. Retry in a few minutes.");
        }

        _logger.LogInformation("Signup for plan {PlanHandle} was rejected as a duplicate; reconciled to existing subscription {SubscriptionId}.",
            planHandle, match.Id);

        return new SubscribeResult(ToSubscription(match), alreadyExisted: true);
    }

    /// <summary>
    /// Relationship invoicing sites call the invoiced collection method <c>remittance</c>; legacy
    /// statement based sites call it <c>invoice</c>. Either way it means "bill the customer, do not
    /// try to charge a stored payment method", which is the only option open to an integration that
    /// captures no card.
    /// </summary>
    private async Task<string> ResolvePaymentCollectionMethodAsync(CancellationToken cancellationToken)
    {
        var site = await ExecuteAsync(() => _siteCache.GetAsync(_client, cancellationToken), "read site configuration", cancellationToken);
        return site.RelationshipInvoicingEnabled ? "remittance" : "invoice";
    }

    private static MaxioSubscription? FindLiveSubscriptionForPlan(IReadOnlyList<MaxioSubscription> subscriptions, string planHandle) =>
        subscriptions.FirstOrDefault(subscription =>
            SubscriptionStates.IsLive(subscription.State) && MatchesPlan(subscription, planHandle));

    private static int CountSubscriptionsForPlan(IReadOnlyList<MaxioSubscription> subscriptions, string planHandle) =>
        subscriptions.Count(subscription => MatchesPlan(subscription, planHandle));

    private static bool MatchesPlan(MaxioSubscription subscription, string planHandle) =>
        string.Equals(subscription.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase);

    private static string BuildCustomerReference(SubscriberAccount account) =>
        ReferenceNamespace + ":" + account.AccountKey;

    /// <summary>
    /// A subscription reference is unique per site, which turns it into a durable idempotency key: two
    /// simultaneous signups derive the same reference, so only one of them can be accepted. The ordinal
    /// keeps that guarantee from becoming permanent - a shopper who cancels and later subscribes to the
    /// same plan again gets the next reference in the series.
    /// </summary>
    private static string BuildSubscriptionReference(SubscriberAccount account, string planHandle, int existingSubscriptionsForPlan) =>
        string.Join(":", ReferenceNamespace, account.AccountKey, planHandle, existingSubscriptionsForPlan.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// A fresh uniqueness token per signup attempt. The token guards the one thing the subscription
    /// reference cannot: a request the transport replays after a timeout, where the server may already
    /// have accepted the first copy. Those replays carry the same buffered body and therefore the same
    /// token, so the server recognises them as the same submission and answers HTTP 409.
    /// </summary>
    /// <remarks>
    /// The token is deliberately not derived from the subscription reference. The server remembers a
    /// token for an hour, which would make a signup fail for the rest of that hour whenever the record
    /// it belongs to disappears in the meantime - as happens when a sandbox site is re-seeded or a
    /// subscription is purged. Duplicate protection that has to survive across instances and restarts
    /// is the reference's job, and the reference is durable.
    /// </remarks>
    private static string BuildUniquenessToken() => Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture);

    private static (string FirstName, string LastName) SplitName(SubscriberAccount account)
    {
        if (!string.IsNullOrWhiteSpace(account.FirstName) || !string.IsNullOrWhiteSpace(account.LastName))
        {
            return (Coalesce(account.FirstName), Coalesce(account.LastName));
        }

        // Both names are required by the API and eShopOnWeb accounts only carry an email address, so the
        // local part stands in for a name until a richer profile exists.
        var localPart = account.Email.Split('@')[0];
        return (string.IsNullOrWhiteSpace(localPart) ? account.Email : localPart, "eShopOnWeb");

        static string Coalesce(string? value) => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
    }

    private static SubscriptionPlan ToPlan(MaxioProduct product) => new(
        handle: product.Handle!,
        name: product.Name ?? product.Handle!,
        description: product.Description,
        priceInCents: product.PriceInCents,
        interval: product.Interval,
        intervalUnit: product.IntervalUnit ?? string.Empty,
        requiresPaymentMethod: product.RequireCreditCard);

    private static CustomerSubscription ToSubscription(MaxioSubscription subscription) => new(
        id: subscription.Id,
        reference: subscription.Reference,
        state: subscription.State ?? string.Empty,
        planHandle: subscription.Product?.Handle ?? string.Empty,
        planName: subscription.Product?.Name ?? string.Empty,
        priceInCents: subscription.ProductPriceInCents,
        currency: subscription.Currency ?? string.Empty,
        interval: subscription.Product?.Interval ?? 0,
        intervalUnit: subscription.Product?.IntervalUnit ?? string.Empty,
        currentPeriodStartedAt: subscription.CurrentPeriodStartedAt,
        currentPeriodEndsAt: subscription.CurrentPeriodEndsAt,
        nextBillingAt: subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
        activatedAt: subscription.ActivatedAt,
        createdAt: subscription.CreatedAt,
        customerId: subscription.Customer?.Id ?? 0,
        paymentCollectionMethod: subscription.PaymentCollectionMethod);

    /// <summary>
    /// Translates transport level failures into the application's own vocabulary. Duplicate signals
    /// (409, and 422 for a taken reference) are deliberately left to propagate so callers can
    /// reconcile them.
    /// </summary>
    private async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, string description, CancellationToken cancellationToken)
    {
        try
        {
            return await operation();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (MaxioDuplicateSubmissionException)
        {
            throw;
        }
        catch (MaxioValidationException ex) when (ex.IsDuplicateReference)
        {
            throw;
        }
        catch (MaxioValidationException ex)
        {
            throw new SubscriptionBillingValidationException(ex.Errors);
        }
        catch (MaxioApiException ex)
        {
            _logger.LogError(ex, "Maxio request failed while trying to {Description}.", description);
            throw new SubscriptionBillingException($"The billing system could not {description}. {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogError(ex, "Maxio was unreachable while trying to {Description}.", description);
            throw new SubscriptionBillingException($"The billing system is currently unreachable, so it could not {description}.", ex);
        }
    }
}
