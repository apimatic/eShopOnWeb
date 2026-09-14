using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Recurring-subscription billing backed by Maxio Advanced Billing.
/// <para>
/// Maxio is the system of record: nothing about plans, customers or subscriptions is stored locally, and
/// every lookup is re-derived from the caller's identity through
/// <see cref="MaxioCustomerReference"/>. Idempotency is layered - a per-subscriber gate serializes
/// concurrent attempts, a read-before-write finds an existing customer or subscription, a write guard stops
/// the SDK from re-sending a write on a transport failure, and any failed write is reconciled by re-reading
/// provider state instead of being assumed to have had no effect.
/// </para>
/// </summary>
public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private const int MaxPages = 50;
    private const int MaxLoggedBodyLength = 512;

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;
    private readonly KeyedAsyncLock _subscriberGate = new();

    public MaxioSubscriptionBillingService(MaxioAdvancedBillingClient client,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyCollection<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var products = await ListFamilyProductsAsync(cancellationToken);

        return products
            .Where(p => p.ArchivedAt is null && !string.IsNullOrWhiteSpace(p.Handle))
            .Select(ToPlan)
            .OrderBy(p => p.Price)
            .ThenBy(p => p.Handle, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(BillingSubscriber subscriber, string? planHandle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        EnsureConfigured();

        var plan = await ResolvePlanAsync(planHandle, cancellationToken);
        var reference = MaxioCustomerReference.For(subscriber.UserName);

        // One subscribe at a time per shopper: without it, two clicks a few milliseconds apart both find
        // "no subscription yet" and both create one.
        using var gate = await _subscriberGate.AcquireAsync(reference, cancellationToken);

        var customer = await EnsureCustomerAsync(subscriber, reference, cancellationToken);
        var customerId = customer.Id ?? throw new BillingProviderException(
            "The billing provider returned a customer without an identifier.", BillingFailure.ProviderError);

        var existing = await FindLiveSubscriptionAsync(customerId, plan.Handle, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation(
                "Maxio subscription {SubscriptionId} on plan {PlanHandle} already exists for customer {CustomerId} in state {State}; returning it unchanged.",
                existing.Id, plan.Handle, customerId, existing.State?.Value);
            return new SubscribeResult(ToSubscription(existing, reference), alreadySubscribed: true);
        }

        try
        {
            var created = await CreateSubscriptionAsync(customerId, plan.Handle, reference, cancellationToken);
            _logger.LogInformation(
                "Created Maxio subscription {SubscriptionId} on plan {PlanHandle} for customer {CustomerId}; state {State}, next billing {NextBillingAt}.",
                created.Id, plan.Handle, customerId, created.State?.Value, created.NextAssessmentAt);
            return new SubscribeResult(ToSubscription(created, reference), alreadySubscribed: false);
        }
        catch (BillingProviderException ex)
        {
            // The write may have taken effect even though we could not confirm it (a blocked resend, an
            // unreadable body, or a rejection because it already exists). Re-read before reporting failure.
            var reconciled = await FindLiveSubscriptionAsync(customerId, plan.Handle, cancellationToken);
            if (reconciled is null)
            {
                throw;
            }

            _logger.LogWarning(ex,
                "Creating a Maxio subscription on plan {PlanHandle} reported a failure, but subscription {SubscriptionId} exists; treating the request as already satisfied.",
                plan.Handle, reconciled.Id);
            return new SubscribeResult(ToSubscription(reconciled, reference), alreadySubscribed: true);
        }
    }

    public async Task<IReadOnlyCollection<CustomerSubscription>> GetSubscriptionsAsync(BillingSubscriber subscriber,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        EnsureConfigured();

        var reference = MaxioCustomerReference.For(subscriber.UserName);
        var customer = await TryReadCustomerAsync(reference, cancellationToken);

        if (customer?.Id is not int customerId)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await ListCustomerSubscriptionsAsync(customerId, cancellationToken);

        return subscriptions
            .Select(s => ToSubscription(s, reference))
            .OrderByDescending(s => s.CreatedAt ?? DateTimeOffset.MinValue)
            .ToList();
    }

    // ---------------------------------------------------------------- plans

    private async Task<SubscriptionPlan> ResolvePlanAsync(string? requestedHandle, CancellationToken cancellationToken)
    {
        var handle = string.IsNullOrWhiteSpace(requestedHandle)
            ? _settings.DefaultPlanHandle?.Trim()
            : requestedHandle!.Trim();

        var plans = await GetPlansAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(handle))
        {
            throw new BillingProviderException(
                "No subscription plan was requested and no default plan is configured.",
                BillingFailure.InvalidRequest,
                details: DescribeAvailablePlans(plans));
        }

        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, handle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new BillingProviderException(
                "'" + handle + "' is not a subscription plan on offer.",
                BillingFailure.InvalidRequest,
                details: DescribeAvailablePlans(plans));
        }

        return plan;
    }

    private static IEnumerable<string> DescribeAvailablePlans(IEnumerable<SubscriptionPlan> plans)
    {
        var handles = plans.Select(p => p.Handle).ToArray();
        return handles.Length == 0
            ? new[] { "no plans are currently on offer" }
            : new[] { "available plans: " + string.Join(", ", handles) };
    }

    private async Task<IReadOnlyList<Product>> ListFamilyProductsAsync(CancellationToken cancellationToken)
    {
        var familyHandle = _settings.ProductFamilyHandle!.Trim();

        try
        {
            return await ListProductsForFamilyAsync("handle:" + familyHandle, cancellationToken);
        }
        catch (BillingProviderException ex) when (ex.Failure == BillingFailure.NotFound)
        {
            // Maxio percent-encodes the colon in the "handle:<x>" selector. If the site does not decode it,
            // resolve the family id at runtime instead - still no numeric id in configuration.
            _logger.LogInformation(
                "Maxio did not resolve product family '{FamilyHandle}' from the handle selector; resolving its id from the family list instead.",
                familyHandle);

            var familyId = await ResolveFamilyIdAsync(familyHandle, cancellationToken);
            return await ListProductsForFamilyAsync(familyId.ToString(CultureInfo.InvariantCulture), cancellationToken);
        }
    }

    private async Task<int> ResolveFamilyIdAsync(string familyHandle, CancellationToken cancellationToken)
    {
        var families = await InvokeAsync("list product families", token =>
            _client.ProductFamilies.ListProductFamilies(
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                ct: token),
            cancellationToken);

        var match = families
            .Select(f => f.ProductFamily)
            .FirstOrDefault(f => f is not null &&
                                 string.Equals(f.Handle, familyHandle, StringComparison.OrdinalIgnoreCase));

        if (match?.Id is not int id)
        {
            throw new BillingProviderException(
                "The configured billing product family was not found.",
                BillingFailure.Configuration,
                details: new[] { MaxioSettings.ConfigurationSection + ":ProductFamilyHandle = '" + familyHandle + "'" });
        }

        return id;
    }

    private async Task<IReadOnlyList<Product>> ListProductsForFamilyAsync(string familySelector,
        CancellationToken cancellationToken)
    {
        var pageSize = Math.Clamp(_settings.PageSize, 1, 200);
        var products = new List<Product>();

        for (var page = 1; page <= MaxPages; page++)
        {
            var pageNumber = page;

            var responses = await InvokeAsync("list plans", async token =>
            {
                try
                {
                    return await _client.ProductFamilies.ListProductsForProductFamily(
                        productFamilyId: familySelector,
                        dateField: null,
                        filter: null,
                        startDate: null,
                        endDate: null,
                        startDatetime: null,
                        endDatetime: null,
                        includeArchived: false,
                        include: null,
                        page: pageNumber,
                        perPage: pageSize,
                        ct: token);
                }
                catch (SdkException<ListProductsForProductFamilyError> ex)
                {
                    throw TranslateListProductsError(ex);
                }
            }, cancellationToken);

            products.AddRange(responses.Select(r => r.Product));

            if (responses.Count < pageSize)
            {
                return products;
            }
        }

        _logger.LogWarning("Stopped paging Maxio products after {MaxPages} pages; the plan list may be incomplete.", MaxPages);
        return products;
    }

    private BillingProviderException TranslateListProductsError(SdkException<ListProductsForProductFamilyError> ex)
    {
        // One branch per accessor the generated error type declares, with the raw fallback last: it is not
        // a catch-all, it only fires for statuses that have no more specific accessor.
        if (ex.Error.TryGetString(out var notFound))
        {
            _logger.LogWarning("Maxio reported the product family as missing: {ProviderMessage}", Truncate(notFound));
            return new BillingProviderException("The configured billing product family was not found.",
                BillingFailure.NotFound, providerStatusCode: 404, innerException: ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return TranslateRawError("list plans", raw, ex);
        }

        _logger.LogError(ex, "Maxio returned an unrecognised error while listing plans.");
        return new BillingProviderException("The billing provider could not list the available plans.",
            BillingFailure.ProviderError, innerException: ex);
    }

    // ------------------------------------------------------------ customers

    private async Task<Customer> EnsureCustomerAsync(BillingSubscriber subscriber, string reference,
        CancellationToken cancellationToken)
    {
        var existing = await TryReadCustomerAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = ResolveName(subscriber);

        try
        {
            using var scope = new MaxioWriteScope("create customer");

            var response = await InvokeAsync("create customer", async token =>
            {
                try
                {
                    return await _client.Customers.CreateCustomer(
                        body: new CreateCustomerRequest
                        {
                            Customer = new CreateCustomer
                            {
                                FirstName = firstName,
                                LastName = lastName,
                                Email = subscriber.Email,
                                Reference = reference
                            }
                        },
                        ct: token);
                }
                catch (SdkException<CreateCustomerError> ex)
                {
                    throw TranslateCreateCustomerError(ex);
                }
            }, cancellationToken);

            _logger.LogInformation("Created Maxio customer {CustomerId} for reference {CustomerReference}.",
                response.Customer.Id, reference);
            return response.Customer;
        }
        catch (BillingProviderException ex)
        {
            // Maxio permits one customer per reference, so a concurrent creation - or a create whose
            // response we could not read - is settled by reading the reference back, not by assuming.
            var reconciled = await TryReadCustomerAsync(reference, cancellationToken);
            if (reconciled is null)
            {
                throw;
            }

            _logger.LogWarning(ex,
                "Creating the Maxio customer for reference {CustomerReference} reported a failure, but customer {CustomerId} exists; continuing with it.",
                reference, reconciled.Id);
            return reconciled;
        }
    }

    private async Task<Customer?> TryReadCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await InvokeAsync("read customer by reference", token =>
                _client.Customers.ReadCustomerByReference(reference: reference, ct: token),
                cancellationToken);

            return response.Customer;
        }
        catch (BillingProviderException ex) when (ex.Failure == BillingFailure.NotFound)
        {
            // Only a genuine 404 means "no such customer". An unreadable response is a different fact and
            // is never treated as absence, because that would turn a bad response into a duplicate create.
            return null;
        }
    }

    private BillingProviderException TranslateCreateCustomerError(SdkException<CreateCustomerError> ex)
    {
        if (ex.Error.TryGetCustomerErrorResponse1(out var body))
        {
            var details = new List<string>();
            if (body?.Errors?.PerPage is { } perPage) details.AddRange(perPage);
            if (body?.Errors?.PricePoint is { } pricePoint) details.AddRange(pricePoint);

            _logger.LogWarning(ex, "Maxio rejected the customer details: {ProviderDetails}",
                details.Count == 0 ? "(no detail)" : string.Join("; ", details));

            return new BillingProviderException("The billing provider rejected the customer details.",
                BillingFailure.InvalidRequest, providerStatusCode: 422, details: details, innerException: ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return TranslateRawError("create customer", raw, ex);
        }

        _logger.LogError(ex, "Maxio returned an unrecognised error while creating a customer.");
        return new BillingProviderException("The billing provider could not create the customer record.",
            BillingFailure.ProviderError, innerException: ex);
    }

    private static (string FirstName, string LastName) ResolveName(BillingSubscriber subscriber)
    {
        var firstName = subscriber.FirstName;
        var lastName = subscriber.LastName;

        if (firstName is not null && lastName is not null)
        {
            return (firstName, lastName);
        }

        var localPart = subscriber.Email.Split('@')[0];
        var parts = localPart.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);

        firstName ??= Titleize(parts.Length > 0 ? parts[0] : localPart);
        lastName ??= parts.Length > 1 ? Titleize(string.Join(" ", parts.Skip(1))) : "Customer";

        // Maxio requires both names; fall back rather than send an empty string.
        if (string.IsNullOrWhiteSpace(firstName)) firstName = "eShopOnWeb";
        if (string.IsNullOrWhiteSpace(lastName)) lastName = "Customer";

        return (firstName, lastName);
    }

    private static string Titleize(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value.Substring(1);

    // -------------------------------------------------------- subscriptions

    private async Task<IReadOnlyList<Subscription>> ListCustomerSubscriptionsAsync(int customerId,
        CancellationToken cancellationToken)
    {
        var responses = await InvokeAsync("list customer subscriptions", token =>
            _client.Customers.ListCustomerSubscriptions(customerId: customerId, ct: token),
            cancellationToken);

        return responses
            .Select(r => r.Subscription)
            .OfType<Subscription>()
            .ToList();
    }

    /// <summary>
    /// States in which a subscription no longer stands. Anything else - active, but also past due,
    /// suspended, on hold - is an existing enrollment that must not be duplicated.
    /// <para>
    /// The set is expressed as the states that <i>end</i> a subscription rather than as
    /// "state == active" on purpose: which state a freshly created remittance subscription lands in is not
    /// documented, and a narrower test would let a second click enroll the shopper twice.
    /// </para>
    /// </summary>
    private static readonly SubscriptionState[] EndedStates =
    {
        SubscriptionState.Canceled,
        SubscriptionState.Expired,
        SubscriptionState.FailedToCreate
    };

    private async Task<Subscription?> FindLiveSubscriptionAsync(int customerId, string planHandle,
        CancellationToken cancellationToken)
    {
        var subscriptions = await ListCustomerSubscriptionsAsync(customerId, cancellationToken);

        return subscriptions.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase) &&
            IsLive(s.State));
    }

    private static bool IsLive(SubscriptionState? state) =>
        state is not null && !EndedStates.Contains(state);

    private async Task<Subscription> CreateSubscriptionAsync(int customerId, string planHandle, string reference,
        CancellationToken cancellationToken)
    {
        var collectionMethod = ResolveCollectionMethod();

        try
        {
            return await CreateSubscriptionAsync(customerId, planHandle, reference, collectionMethod, cancellationToken);
        }
        catch (BillingProviderException ex) when (ShouldRetryWithLegacyCollectionMethod(ex, collectionMethod))
        {
            // Which no-payment-profile collection method a site accepts depends on its billing
            // architecture: current sites take "remittance", legacy statement sites take "invoice". Only
            // the provider knows which, so fall back once when it says this value is not one of its own.
            _logger.LogInformation(
                "Maxio rejected collection method '{Rejected}'; retrying once with '{Fallback}' for a legacy statements site.",
                collectionMethod.Value, CollectionMethod.Invoice.Value);

            return await CreateSubscriptionAsync(customerId, planHandle, reference, CollectionMethod.Invoice,
                cancellationToken);
        }
    }

    private async Task<Subscription> CreateSubscriptionAsync(int customerId, string planHandle, string reference,
        CollectionMethod collectionMethod, CancellationToken cancellationToken)
    {
        using var scope = new MaxioWriteScope("create subscription");

        var response = await InvokeAsync("create subscription", async token =>
        {
            try
            {
                return await _client.Subscriptions.CreateSubscription(
                    body: new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            // By handle and by customer id: the two identifiers that stay valid when the
                            // site is re-seeded and numeric product ids are reassigned.
                            ProductHandle = planHandle,
                            CustomerId = customerId,
                            // Without this Maxio bills automatically and rejects the signup because no
                            // payment method is on file. The product's credit-card flags do not govern
                            // that; the collection method does.
                            PaymentCollectionMethod = collectionMethod
                        }
                    },
                    ct: token);
            }
            catch (SdkException<CreateSubscriptionError> ex)
            {
                throw TranslateCreateSubscriptionError(ex);
            }
        }, cancellationToken);

        if (response.Subscription is { } subscription)
        {
            return subscription;
        }

        _logger.LogWarning("Maxio accepted the subscription for {CustomerReference} but returned no subscription body.",
            reference);

        var reconciled = await FindLiveSubscriptionAsync(customerId, planHandle, cancellationToken);
        return reconciled ?? throw new BillingProviderException(
            "The billing provider accepted the subscription but did not return it.",
            BillingFailure.ProviderError);
    }

    /// <summary>
    /// Maps the configured collection method onto the SDK's string enum. Mapped explicitly rather than
    /// parsed, so an unusable value is reported as the configuration mistake it is instead of being sent
    /// to the provider and coming back as a puzzling rejection.
    /// </summary>
    private CollectionMethod ResolveCollectionMethod()
    {
        var configured = _settings.PaymentCollectionMethod?.Trim();

        if (string.IsNullOrEmpty(configured))
        {
            return CollectionMethod.Remittance;
        }

        if (Matches(configured, CollectionMethod.Remittance)) return CollectionMethod.Remittance;
        if (Matches(configured, CollectionMethod.Invoice)) return CollectionMethod.Invoice;
        if (Matches(configured, CollectionMethod.Automatic)) return CollectionMethod.Automatic;
        if (Matches(configured, CollectionMethod.Prepaid)) return CollectionMethod.Prepaid;

        throw new BillingProviderException("Subscription billing is not configured correctly on this deployment.",
            BillingFailure.Configuration,
            details: new[]
            {
                MaxioSettings.ConfigurationSection + ":PaymentCollectionMethod must be one of " +
                string.Join(", ", CollectionMethod.Remittance.Value, CollectionMethod.Invoice.Value,
                    CollectionMethod.Automatic.Value, CollectionMethod.Prepaid.Value)
            });

        static bool Matches(string configured, CollectionMethod candidate) =>
            string.Equals(configured, candidate.Value, StringComparison.OrdinalIgnoreCase);
    }

    private bool ShouldRetryWithLegacyCollectionMethod(BillingProviderException exception, CollectionMethod attempted) =>
        exception.Failure == BillingFailure.InvalidRequest &&
        // Only when the value was this integration's own default; an explicitly configured method is honoured.
        string.IsNullOrWhiteSpace(_settings.PaymentCollectionMethod) &&
        attempted == CollectionMethod.Remittance &&
        exception.Details.Any(d => d.Contains("collection method", StringComparison.OrdinalIgnoreCase) ||
                                   d.Contains("payment_collection_method", StringComparison.OrdinalIgnoreCase));

    private BillingProviderException TranslateCreateSubscriptionError(SdkException<CreateSubscriptionError> ex)
    {
        if (ex.Error.TryGetErrorListResponse1(out var body))
        {
            var details = body?.Errors ?? (IReadOnlyList<string>)Array.Empty<string>();

            _logger.LogWarning(ex, "Maxio rejected the subscription: {ProviderDetails}",
                details.Count == 0 ? "(no detail)" : string.Join("; ", details));

            return new BillingProviderException("The billing provider rejected the subscription.",
                BillingFailure.InvalidRequest, providerStatusCode: 422, details: details, innerException: ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return TranslateRawError("create subscription", raw, ex);
        }

        _logger.LogError(ex, "Maxio returned an unrecognised error while creating a subscription.");
        return new BillingProviderException("The billing provider could not create the subscription.",
            BillingFailure.ProviderError, innerException: ex);
    }

    // --------------------------------------------------------- call plumbing

    /// <summary>
    /// Runs one provider call under a total time budget and converts every way it can fail into a
    /// <see cref="BillingProviderException"/>, so callers have one failure type rather than four.
    /// </summary>
    private async Task<T> InvokeAsync<T>(string operation, Func<CancellationToken, Task<T>> call,
        CancellationToken cancellationToken)
    {
        // The per-attempt timeouts bound one stalled socket; only this bounds what the caller waits for.
        // Linked to the caller's token so a disconnected client also stops the outbound work.
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _settings.CallBudgetSeconds)));

        try
        {
            return await call(budget.Token);
        }
        catch (BillingProviderException)
        {
            throw;
        }
        catch (Exception ex) when (FindBlockedResend(ex) is not null)
        {
            _logger.LogWarning(ex,
                "Maxio {Operation}: a resend was blocked after a transport failure. Exactly one request was sent and its outcome is unknown.",
                operation);
            throw new BillingProviderException(
                "The billing provider did not confirm the request; its outcome is unknown.",
                BillingFailure.Unavailable, innerException: ex);
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRawError(operation, ex.Error, ex);
        }
        catch (JsonException ex)
        {
            throw TranslateUnreadableResponse(operation, ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Maxio {Operation} exceeded the {Budget}s call budget.", operation,
                _settings.CallBudgetSeconds);
            throw new BillingProviderException("The billing provider did not respond in time.",
                BillingFailure.Unavailable, innerException: ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Maxio {Operation} could not reach the provider.", operation);
            throw new BillingProviderException("The billing provider could not be reached.",
                BillingFailure.Unavailable, innerException: ex);
        }
    }

    private BillingProviderException TranslateRawError(string operation, RawError raw, Exception source)
    {
        var status = (int)raw.StatusCode;
        var body = ReadBodySafely(raw);

        if (status is 401 or 403)
        {
            // The provider rejected *our* credentials. That is a deployment problem, and it must never be
            // reflected back to the caller as if their own token were at fault.
            _logger.LogError(source, "Maxio {Operation} was refused with HTTP {StatusCode}; check the configured API key and site. {Body}",
                operation, status, body);
            return new BillingProviderException(
                "The billing provider rejected this application's credentials.",
                BillingFailure.Configuration, status, innerException: source);
        }

        _logger.LogWarning(source, "Maxio {Operation} failed with HTTP {StatusCode}. {Body}", operation, status, body);

        return status switch
        {
            404 => new BillingProviderException("The billing provider has no such record.",
                BillingFailure.NotFound, status, innerException: source),
            409 => new BillingProviderException("The billing provider reported a conflicting state.",
                BillingFailure.Conflict, status, innerException: source),
            400 or 422 => new BillingProviderException("The billing provider rejected the request.",
                BillingFailure.InvalidRequest, status, innerException: source),
            408 or 429 => new BillingProviderException("The billing provider is busy; try again shortly.",
                BillingFailure.Unavailable, status, innerException: source),
            >= 500 => new BillingProviderException("The billing provider is currently unavailable.",
                BillingFailure.Unavailable, status, innerException: source),
            _ => new BillingProviderException("The billing provider returned an unexpected response.",
                BillingFailure.ProviderError, status, innerException: source)
        };
    }

    /// <summary>
    /// A <see cref="JsonException"/> reaches this boundary from two directions that need opposite answers:
    /// a 2xx whose body no longer matches the model (outcome genuinely unknown), and a non-2xx whose body
    /// did not match the generated error shape - in which case the exception replaced the SDK exception and
    /// took the HTTP status with it. Reporting the second as an outage would tell a caller to keep retrying
    /// a request that can never succeed, so the status observed on the wire is used to tell them apart.
    /// </summary>
    private BillingProviderException TranslateUnreadableResponse(string operation, JsonException ex)
    {
        var observed = MaxioWriteScope.Current?.ObservedStatusCode;

        if (observed is >= 400)
        {
            _logger.LogWarning(ex,
                "Maxio {Operation} was rejected with HTTP {StatusCode} and the error body did not match the expected shape.",
                operation, observed);

            return observed switch
            {
                404 => new BillingProviderException("The billing provider has no such record.",
                    BillingFailure.NotFound, observed, innerException: ex),
                409 => new BillingProviderException("The billing provider reported a conflicting state.",
                    BillingFailure.Conflict, observed, innerException: ex),
                401 or 403 => new BillingProviderException("The billing provider rejected this application's credentials.",
                    BillingFailure.Configuration, observed, innerException: ex),
                >= 500 => new BillingProviderException("The billing provider is currently unavailable.",
                    BillingFailure.Unavailable, observed, innerException: ex),
                _ => new BillingProviderException("The billing provider rejected the request.",
                    BillingFailure.InvalidRequest, observed, innerException: ex)
            };
        }

        _logger.LogError(ex, "Maxio {Operation} returned a response that could not be read.", operation);
        return new BillingProviderException("The billing provider returned a response that could not be processed.",
            BillingFailure.ProviderError, observed, innerException: ex);
    }

    private static MaxioWriteResendBlockedException? FindBlockedResend(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is MaxioWriteResendBlockedException blocked)
            {
                return blocked;
            }
        }

        return null;
    }

    private static string ReadBodySafely(RawError raw)
    {
        try
        {
            return Truncate(raw.ReadAsString());
        }
        catch (Exception)
        {
            return "(error body unavailable)";
        }
    }

    private static string Truncate(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "(empty)";
        return value!.Length <= MaxLoggedBodyLength ? value : value.Substring(0, MaxLoggedBodyLength) + "...";
    }

    private void EnsureConfigured()
    {
        var problem = _settings.Validate();
        if (problem is not null)
        {
            throw new BillingProviderException("Subscription billing is not configured on this deployment.",
                BillingFailure.Configuration, details: new[] { problem });
        }
    }

    // ------------------------------------------------------------- mapping

    private SubscriptionPlan ToPlan(Product product) => new()
    {
        Handle = product.Handle!,
        Name = string.IsNullOrWhiteSpace(product.Name) ? product.Handle! : product.Name!,
        Description = product.Description,
        Price = ToCurrency(product.PriceInCents),
        Interval = product.Interval ?? 0,
        IntervalUnit = product.IntervalUnit?.Value ?? string.Empty,
        // Two separately generated card flags exist and only live traffic says which one a site populates,
        // so require a payment method when either says so and treat "neither" as not required.
        RequiresPaymentMethod = product.RequireCreditCard ?? product.RequestCreditCard ?? false,
        ProductFamilyHandle = product.ProductFamily?.Handle ?? _settings.ProductFamilyHandle
    };

    private static CustomerSubscription ToSubscription(Subscription subscription, string reference) => new()
    {
        Id = subscription.Id ?? 0,
        State = subscription.State?.Value ?? "unknown",
        PlanHandle = subscription.Product?.Handle,
        PlanName = subscription.Product?.Name,
        // The subscription carries its own price snapshot; prefer it over the product's current price.
        Price = ToCurrency(subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents),
        Interval = subscription.Product?.Interval ?? 0,
        IntervalUnit = subscription.Product?.IntervalUnit?.Value ?? string.Empty,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        // Maxio returns no next_billing_at on a subscription; the next assessment is that date.
        NextBillingAt = subscription.NextAssessmentAt,
        CreatedAt = subscription.CreatedAt,
        PaymentCollectionMethod = subscription.PaymentCollectionMethod?.Value,
        CustomerReference = subscription.Customer?.Reference ?? reference,
        CustomerEmail = subscription.Customer?.Email
    };

    private static decimal ToCurrency(long? cents) => (cents ?? 0L) / 100m;
}
