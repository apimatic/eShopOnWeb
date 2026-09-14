using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Subscription billing backed by Maxio Advanced Billing, which is the system of record: eShopOnWeb
/// persists no subscription state, so every read reflects Maxio and nothing can drift.
/// </summary>
/// <remarks>
/// <para>
/// Subscribe is idempotent through three layers, each covering what the one before it cannot:
/// </para>
/// <list type="number">
/// <item>a per-user in-process lock, so concurrent requests on one instance queue up instead of
/// racing;</item>
/// <item>a read-before-write check for an existing live subscription to the same plan, which holds
/// across instances, restarts and any length of time;</item>
/// <item>an application-chosen <c>reference</c> on both writes. Maxio enforces references as unique
/// per site, so the loser of a genuine race is refused with 422 and reads the winner back. This is
/// the layer that makes the guarantee real rather than best-effort, and unlike an in-memory record
/// it survives the process.</item>
/// </list>
/// <para>
/// Maxio also offers a <c>uniqueness_token</c> replay guard, which this integration deliberately does
/// not use: the token is consumed even when the request it accompanied was rejected, so one failed
/// attempt would lock the shopper out of retrying the same subscribe for the length of the replay
/// window. The unique reference gives stronger protection with no such trap.
/// </para>
/// </remarks>
internal sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private const string SiteCurrencyCacheKey = "Maxio:SiteCurrency";

    /// <summary>Fallback if the site ever reports no currency; Maxio quotes USD by default.</summary>
    private const string FallbackCurrency = "USD";

    /// <summary>Last resort when no name can be derived at all - Maxio rejects a blank one.</summary>
    private const string UnknownNamePlaceholder = "eShopOnWeb";

    private static readonly char[] NameSeparators = { '.', '_', '-', '+' };

    /// <summary>Above this length a subscription reference is replaced by a digest of itself.</summary>
    private const int MaxReadableReferenceLength = 120;

    private readonly IMaxioApiClient _client;
    private readonly MaxioSettings _settings;
    private readonly IMemoryCache _cache;
    private readonly KeyedAsyncLock _subscribeLock;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        IMaxioApiClient client,
        IOptions<MaxioSettings> settings,
        IMemoryCache cache,
        KeyedAsyncLock subscribeLock,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _cache = cache;
        _subscribeLock = subscribeLock;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var currency = await GetSiteCurrencyAsync(cancellationToken).ConfigureAwait(false);
        var products = await ExecuteAsync(
            () => _client.ListProductsForFamilyAsync(_settings.ProductFamilyHandle, cancellationToken),
            "list subscription plans").ConfigureAwait(false);

        return products
            .Where(p => p.ArchivedAt is null && !string.IsNullOrWhiteSpace(p.Handle))
            .OrderBy(p => p.PriceInCents)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p => ToPlan(p, currency))
            .ToList();
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsForUserAsync(string userName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new ArgumentException("User name is required.", nameof(userName));
        }

        var reference = BuildCustomerReference(userName);
        var currency = await GetSiteCurrencyAsync(cancellationToken).ConfigureAwait(false);

        var customer = await ExecuteAsync(
            () => _client.FindCustomerByReferenceAsync(reference, cancellationToken),
            "look up the billing customer").ConfigureAwait(false);

        if (customer is null)
        {
            // The shopper has never subscribed, so no billing customer exists yet. That is not an
            // error - it is an empty list.
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await ExecuteAsync(
            () => _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken),
            "list the customer's subscriptions").ConfigureAwait(false);

        return subscriptions
            .OrderByDescending(s => s.ActivatedAt ?? s.CurrentPeriodStartedAt ?? DateTimeOffset.MinValue)
            .ThenByDescending(s => s.Id)
            .Select(s => ToSubscription(s, currency, customer))
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var reference = BuildCustomerReference(request.UserName);
        var currency = await GetSiteCurrencyAsync(cancellationToken).ConfigureAwait(false);

        // Resolve the plan before touching customer records: an unknown handle should fail without
        // leaving a customer behind.
        var plan = await ResolvePlanAsync(request.PlanHandle, cancellationToken).ConfigureAwait(false);

        using var _ = await _subscribeLock.AcquireAsync(reference, cancellationToken).ConfigureAwait(false);

        var customer = await EnsureCustomerAsync(request, reference, cancellationToken).ConfigureAwait(false);

        var planHandle = plan.Handle!;
        var subscriptions = await ExecuteAsync(
            () => _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken),
            "check for an existing subscription").ConfigureAwait(false);

        var existing = FindReusable(subscriptions, planHandle);
        if (existing is not null)
        {
            _logger.LogInformation(
                "Reusing Maxio subscription {SubscriptionId} ({State}) on plan {PlanHandle} for customer {CustomerId}.",
                existing.Id, existing.State, planHandle, customer.Id);

            return new SubscribeResult(ToSubscription(existing, currency, customer), created: false);
        }

        var subscriptionReference = BuildSubscriptionReference(reference, planHandle, request.IdempotencyKey, subscriptions);
        var created = await CreateSubscriptionAsync(planHandle, subscriptionReference, customer, cancellationToken).ConfigureAwait(false);
        return new SubscribeResult(ToSubscription(created.Subscription, currency, customer), created.Created);
    }

    private async Task<MaxioProduct> ResolvePlanAsync(string planHandle, CancellationToken cancellationToken)
    {
        var products = await ExecuteAsync(
            () => _client.ListProductsForFamilyAsync(_settings.ProductFamilyHandle, cancellationToken),
            "resolve the subscription plan").ConfigureAwait(false);

        var plan = products.FirstOrDefault(p =>
            string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase) && p.ArchivedAt is null);

        if (plan is null)
        {
            throw new PlanNotFoundException(planHandle);
        }

        return plan;
    }

    /// <summary>
    /// Returns the billing customer for this shopper, creating it only if Maxio does not already
    /// have one under the derived reference.
    /// </summary>
    private async Task<MaxioCustomer> EnsureCustomerAsync(SubscribeRequest request, string reference, CancellationToken cancellationToken)
    {
        var existing = await ExecuteAsync(
            () => _client.FindCustomerByReferenceAsync(reference, cancellationToken),
            "look up the billing customer").ConfigureAwait(false);

        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = DeriveName(request);
        var createRequest = new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCreateCustomer
            {
                Reference = reference,
                Email = string.IsNullOrWhiteSpace(request.Email) ? request.UserName : request.Email,
                FirstName = firstName,
                LastName = lastName
            }
        };

        try
        {
            var created = await _client.CreateCustomerAsync(createRequest, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Created Maxio customer {CustomerId} for reference {CustomerReference}.", created.Id, reference);
            return created;
        }
        catch (MaxioApiException ex) when (ex.IsReferenceTaken)
        {
            // Another request created this customer between our lookup and our write. Read the winner
            // rather than failing - that is the whole point of keying on a stable reference.
            _logger.LogInformation("Maxio already had a customer for reference {CustomerReference}; reusing it.", reference);

            var winner = await ExecuteAsync(
                () => _client.FindCustomerByReferenceAsync(reference, cancellationToken),
                "re-read the billing customer").ConfigureAwait(false);

            return winner ?? throw new BillingProviderException(
                "Maxio reported the customer reference as already taken but did not return the customer.",
                (int)ex.StatusCode);
        }
        catch (MaxioApiException ex)
        {
            throw Translate(ex, "create the billing customer");
        }
    }

    private async Task<(MaxioSubscription Subscription, bool Created)> CreateSubscriptionAsync(
        string planHandle, string subscriptionReference, MaxioCustomer customer, CancellationToken cancellationToken)
    {
        var createRequest = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscription
            {
                ProductHandle = planHandle,
                CustomerId = customer.Id,
                PaymentCollectionMethod = _settings.PaymentCollectionMethod,
                Reference = subscriptionReference
            }
        };

        try
        {
            var subscription = await _client.CreateSubscriptionAsync(createRequest, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "Created Maxio subscription {SubscriptionId} ({State}) on plan {PlanHandle} for customer {CustomerId}.",
                subscription.Id, subscription.State, planHandle, customer.Id);

            return (subscription, true);
        }
        catch (MaxioApiException ex) when (ex.IsReferenceTaken)
        {
            // Another attempt at this same subscribe got there first - either a concurrent request on
            // another instance, or a retry of a write whose response we never saw. Either way the
            // subscription exists under the reference we chose, so read it back.
            _logger.LogInformation(
                "Maxio already had a subscription under reference {SubscriptionReference}; reading it back instead of creating another.",
                subscriptionReference);

            var winner = await ExecuteAsync(
                () => _client.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken),
                "read back the existing subscription").ConfigureAwait(false);

            if (winner is not null)
            {
                return (winner, false);
            }

            throw new DuplicateSubscribeRequestException(planHandle);
        }
        catch (MaxioApiException ex)
        {
            throw Translate(ex, "create the subscription");
        }
    }

    /// <summary>
    /// Picks a subscription on this plan that the shopper is still enrolled in. Ended subscriptions
    /// (canceled, expired, failed) are ignored, so re-subscribing after a cancellation works.
    /// </summary>
    private static MaxioSubscription? FindReusable(IEnumerable<MaxioSubscription> subscriptions, string planHandle) =>
        subscriptions
            .Where(s => string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase))
            .Where(s => SubscriptionStates.BlocksResubscribe(s.State))
            .OrderByDescending(s => s.Id)
            .FirstOrDefault();

    private async Task<string> GetSiteCurrencyAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(SiteCurrencyCacheKey, out string? cached) && !string.IsNullOrWhiteSpace(cached))
        {
            return cached;
        }

        // Products carry a price but no currency, so the site's default currency is what plan and
        // subscription prices are quoted in. It never changes in practice, hence the cache.
        var site = await ExecuteAsync(() => _client.GetSiteAsync(cancellationToken), "read the Maxio site settings")
            .ConfigureAwait(false);

        var currency = string.IsNullOrWhiteSpace(site.Currency) ? FallbackCurrency : site.Currency;
        _cache.Set(SiteCurrencyCacheKey, currency, _settings.SiteCacheDuration);
        return currency;
    }

    /// <summary>
    /// Derives the Maxio customer reference from the eShopOnWeb user name. The user name is stable
    /// across restarts - unlike the Identity row id, which the in-memory provider regenerates - so
    /// the same shopper always maps to the same billing customer.
    /// </summary>
    private string BuildCustomerReference(string userName) =>
        _settings.CustomerReferencePrefix + userName.Trim().ToLowerInvariant();

    /// <summary>
    /// Derives the reference the new subscription will be created under. Maxio enforces it as unique
    /// per site, so this value - not a timestamp or a random id - is what makes a repeated subscribe
    /// resolve to one subscription instead of two.
    /// </summary>
    /// <remarks>
    /// The reference must be identical for every attempt at the same intent, yet different for a
    /// deliberate later signup. A caller-supplied idempotency key defines the intent when present;
    /// otherwise the count of the shopper's prior subscriptions to this plan acts as a generation
    /// number, which is the same for concurrent attempts and increments once a previous subscription
    /// has ended and the shopper signs up again.
    /// </remarks>
    private static string BuildSubscriptionReference(
        string customerReference,
        string planHandle,
        string? idempotencyKey,
        IEnumerable<MaxioSubscription> existingSubscriptions)
    {
        var scope = string.IsNullOrWhiteSpace(idempotencyKey)
            ? existingSubscriptions
                .Count(s => string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase))
                .ToString(CultureInfo.InvariantCulture)
            : idempotencyKey.Trim();

        var reference = $"{customerReference}|{planHandle.ToLowerInvariant()}|{scope}";

        // Keep the reference readable in the Maxio UI where it fits, and fall back to a stable digest
        // when a long e-mail address or idempotency key would make it unwieldy.
        return reference.Length <= MaxReadableReferenceLength ? reference : Digest(reference);
    }

    /// <summary>Stable, collision-resistant stand-in for a reference that is too long to keep readable.</summary>
    private static string Digest(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return $"eshoponweb|sha256|{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    /// <summary>
    /// Produces the given/family name pair for the billing customer. Maxio rejects a customer whose
    /// first or last name is blank, but eShopOnWeb identities carry no name, so anything the caller
    /// did not supply is derived from the e-mail address - enough for the record to be recognisable
    /// in the Maxio UI, and stable for the same shopper every time.
    /// </summary>
    private static (string FirstName, string LastName) DeriveName(SubscribeRequest request)
    {
        var suppliedFirst = NullIfBlank(request.FirstName);
        var suppliedLast = NullIfBlank(request.LastName);
        if (suppliedFirst is not null && suppliedLast is not null)
        {
            return (suppliedFirst, suppliedLast);
        }

        var address = NullIfBlank(request.Email) ?? request.UserName;
        var atIndex = address.IndexOf('@');
        var localPart = atIndex < 0 ? address : address[..atIndex];
        var domain = atIndex < 0 ? string.Empty : address[(atIndex + 1)..];

        // "john.doe@example.com" reads as two names; "demouser@example.com" gives only one, so the
        // domain's first label stands in for the family name.
        var tokens = localPart.Split(NameSeparators, StringSplitOptions.RemoveEmptyEntries);
        var derivedFirst = tokens.Length > 0 ? Capitalize(tokens[0]) : null;
        var derivedLast = tokens.Length > 1
            ? Capitalize(string.Join(' ', tokens[1..]))
            : Capitalize(domain.Split('.', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault());

        return (suppliedFirst ?? derivedFirst ?? UnknownNamePlaceholder,
                suppliedLast ?? derivedLast ?? UnknownNamePlaceholder);
    }

    private static string? Capitalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return char.ToUpperInvariant(value[0]) + value[1..];
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static SubscriptionPlan ToPlan(MaxioProduct product, string currency) => new(
        handle: product.Handle!,
        name: product.Name ?? product.Handle!,
        description: product.Description,
        priceInCents: product.PriceInCents,
        currency: currency,
        intervalLength: product.Interval,
        intervalUnit: product.IntervalUnit,
        requiresPaymentMethod: product.RequireCreditCard);

    private static CustomerSubscription ToSubscription(MaxioSubscription subscription, string siteCurrency, MaxioCustomer customer)
    {
        var product = subscription.Product;

        return new CustomerSubscription(
            id: subscription.Id,
            state: subscription.State ?? "unknown",
            planHandle: product?.Handle ?? string.Empty,
            planName: product?.Name ?? product?.Handle ?? string.Empty,
            priceInCents: subscription.ProductPriceInCents,
            currency: string.IsNullOrWhiteSpace(subscription.Currency) ? siteCurrency : subscription.Currency,
            intervalLength: product?.Interval,
            intervalUnit: product?.IntervalUnit,
            currentPeriodStartedAt: subscription.CurrentPeriodStartedAt,
            currentPeriodEndsAt: subscription.CurrentPeriodEndsAt,
            nextBillingAt: subscription.NextAssessmentAt,
            activatedAt: subscription.ActivatedAt,
            canceledAt: subscription.CanceledAt,
            balanceInCents: subscription.BalanceInCents,
            paymentCollectionMethod: subscription.PaymentCollectionMethod,
            billingCustomerId: subscription.Customer?.Id ?? customer.Id,
            billingCustomerReference: subscription.Customer?.Reference ?? customer.Reference);
    }

    /// <summary>Runs a client call and translates any provider failure into an ApplicationCore exception.</summary>
    private async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, string description)
    {
        try
        {
            return await operation().ConfigureAwait(false);
        }
        catch (MaxioApiException ex)
        {
            throw Translate(ex, description);
        }
    }

    private BillingException Translate(MaxioApiException ex, string description)
    {
        _logger.LogError(ex, "Maxio call failed while attempting to {Description}.", description);

        // 422 is Maxio telling us the request itself is not acceptable - for example, no payment
        // method on file. That is actionable by the caller, so it stays distinct from an outage.
        if (ex.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
        {
            return new BillingValidationException(ex.Errors);
        }

        var detail = ex.Errors.Count > 0 ? $" ({string.Join("; ", ex.Errors)})" : string.Empty;
        return new BillingProviderException(
            $"The billing system could not {description}{detail}.",
            (int)ex.StatusCode);
    }
}
