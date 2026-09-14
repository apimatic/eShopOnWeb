using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Maxio = MaxioAdvancedBilling.Models;
using MaxioEnums = MaxioAdvancedBilling.Models.Enums;
using MaxioErrors = MaxioAdvancedBilling.Errors;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// The Maxio Advanced Billing implementation of <see cref="ISubscriptionBillingService"/>, and the
/// only place in eShopOnWeb that talks to the billing SDK. Every SDK failure - an API error, a
/// transport failure, a body that cannot be read - is translated here into a
/// <see cref="SubscriptionBillingException"/> carrying a caller-safe message and the status this
/// application should answer with.
/// </summary>
public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    /// <summary>
    /// States in which a subscription no longer bills, so the shopper may subscribe again.
    /// Anything else - including a state this build does not recognise - counts as live, because
    /// the cost of wrongly treating a live subscription as gone is billing the shopper twice.
    /// </summary>
    private static readonly HashSet<string> TerminalStates = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        MaxioEnums.SubscriptionState.Canceled.Value,
        MaxioEnums.SubscriptionState.Expired.Value,
        MaxioEnums.SubscriptionState.FailedToCreate.Value,
        MaxioEnums.SubscriptionState.TrialEnded.Value
    };

    /// <summary>The whole-call budget. The SDK's own timeouts bound a single attempt, never the call.</summary>
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    private const int PlanPageSize = 100;
    private const int MaxPlanPages = 20;

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly SubscriberLockRegistry _subscriberLocks;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioSettings> settings,
        SubscriberLockRegistry subscriberLocks,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = Guard.Against.Null(client, nameof(client));
        _settings = Guard.Against.Null(settings, nameof(settings)).Value;
        _subscriberLocks = Guard.Against.Null(subscriberLocks, nameof(subscriberLocks));
        _logger = Guard.Against.Null(logger, nameof(logger));
    }

    public Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default) =>
        WithBudgetAsync(ListPlansCoreAsync, cancellationToken);

    public Task<SubscribeResult> SubscribeAsync(Subscriber subscriber, string planHandle, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(subscriber, nameof(subscriber));
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));

        return WithBudgetAsync(ct => SubscribeCoreAsync(subscriber, planHandle.Trim(), ct), cancellationToken);
    }

    public Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(Subscriber subscriber, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(subscriber, nameof(subscriber));

        return WithBudgetAsync(ct => ListSubscriptionsCoreAsync(subscriber, ct), cancellationToken);
    }

    // ---------------------------------------------------------------- flows

    private async Task<IReadOnlyList<SubscriptionPlan>> ListPlansCoreAsync(CancellationToken cancellationToken)
    {
        var products = await ListFamilyProductsAsync(cancellationToken);

        return products
            .Where(product => !string.IsNullOrWhiteSpace(product.Handle) && product.ArchivedAt is null)
            .Select(MapPlan)
            .OrderBy(plan => plan.PriceInCents)
            .ThenBy(plan => plan.Handle, StringComparer.Ordinal)
            .ToList();
    }

    private async Task<SubscribeResult> SubscribeCoreAsync(Subscriber subscriber, string planHandle, CancellationToken cancellationToken)
    {
        // Resolve the plan against the configured family first: it turns a bad handle into a clean
        // 404 listing what is on offer, and stops a caller subscribing to a product outside it.
        var plans = await ListPlansCoreAsync(cancellationToken);
        var plan = plans.FirstOrDefault(candidate => string.Equals(candidate.Handle, planHandle, StringComparison.OrdinalIgnoreCase));

        if (plan is null)
        {
            throw new SubscriptionBillingException(
                $"Plan '{planHandle}' is not available for subscription.",
                HttpStatusCode.NotFound,
                details: plans.Select(available => available.Handle).ToList());
        }

        // Serialize concurrent subscribes for this shopper so the read-then-write below cannot be
        // interleaved by a second click.
        using var subscriberLock = await _subscriberLocks.AcquireAsync(subscriber.Reference, cancellationToken);

        var customer = await EnsureCustomerAsync(subscriber, cancellationToken);
        var customerId = RequireCustomerId(customer);

        var existing = await FindLiveSubscriptionAsync(customerId, plan.Handle, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation(
                "Subscriber {SubscriberReference} already has live subscription {SubscriptionId} on plan {PlanHandle}; not creating another.",
                subscriber.Reference,
                existing.Id,
                plan.Handle);

            return new SubscribeResult(existing, alreadySubscribed: true);
        }

        var created = await CreateSubscriptionAsync(subscriber, customerId, plan.Handle, cancellationToken);
        return new SubscribeResult(created, alreadySubscribed: false);
    }

    private async Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsCoreAsync(Subscriber subscriber, CancellationToken cancellationToken)
    {
        var customer = await FindCustomerByReferenceAsync(subscriber.Reference, cancellationToken);
        if (customer?.Id is null)
        {
            // Never subscribed, so there is no billing customer. That is an empty list, not a failure.
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await ListCustomerSubscriptionsAsync(customer.Id.Value, cancellationToken);

        return subscriptions
            .Select(MapSubscription)
            .OrderByDescending(subscription => subscription.CreatedAt ?? DateTimeOffset.MinValue)
            .ToList();
    }

    private async Task<Maxio.Customer> EnsureCustomerAsync(Subscriber subscriber, CancellationToken cancellationToken)
    {
        var existing = await FindCustomerByReferenceAsync(subscriber.Reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            var created = await CreateCustomerAsync(subscriber, cancellationToken);
            _logger.LogInformation(
                "Created billing customer {CustomerId} for subscriber {SubscriberReference}.",
                created.Id,
                subscriber.Reference);

            return created;
        }
        catch (SubscriptionBillingException ex)
        {
            // The provider enforces uniqueness on the customer reference, so a rejected create is
            // very often a lost race with a concurrent request rather than a real failure. The
            // status a duplicate reference produces is not documented, so re-read on any failure
            // and only surface the error when the customer really is not there.
            var raced = await FindCustomerByReferenceAsync(subscriber.Reference, cancellationToken);
            if (raced is not null)
            {
                _logger.LogInformation(
                    "Create-customer for {SubscriberReference} was rejected (provider status {ProviderStatus}) but the customer now exists; continuing with it.",
                    subscriber.Reference,
                    ex.ProviderStatusCode);

                return raced;
            }

            throw;
        }
    }

    private async Task<CustomerSubscription?> FindLiveSubscriptionAsync(int customerId, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await ListCustomerSubscriptionsAsync(customerId, cancellationToken);

        return subscriptions
            .Select(MapSubscription)
            .Where(subscription => subscription.IsLive
                && string.Equals(subscription.PlanHandle, planHandle, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(subscription => subscription.CreatedAt ?? DateTimeOffset.MinValue)
            .FirstOrDefault();
    }

    /// <summary>
    /// Settles a write whose outcome is unknown by re-reading provider state, rather than telling
    /// the caller it definitely failed. A blocked resend, a dropped connection or an unreadable
    /// body all leave the same question: did the subscription get created or not?
    /// </summary>
    private async Task<CustomerSubscription> ReconcileAfterUncertainCreateAsync(
        int customerId,
        string planHandle,
        Exception cause,
        CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            cause,
            "Create-subscription for customer {CustomerId} on plan {PlanHandle} ended with an unknown outcome; reconciling against the provider.",
            customerId,
            planHandle);

        try
        {
            var existing = await FindLiveSubscriptionAsync(customerId, planHandle, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation(
                    "Reconciliation found subscription {SubscriptionId}; the uncertain write had in fact succeeded.",
                    existing.Id);

                return existing;
            }
        }
        catch (SubscriptionBillingException reconcileFailure)
        {
            _logger.LogError(
                reconcileFailure,
                "Reconciliation after an uncertain create-subscription failed for customer {CustomerId}.",
                customerId);
        }

        throw new SubscriptionBillingException(
            "The subscription could not be confirmed with the billing provider and no subscription was found afterwards. Please retry.",
            HttpStatusCode.ServiceUnavailable,
            innerException: cause);
    }

    // ------------------------------------------------------------ SDK calls

    private async Task<IReadOnlyList<Maxio.Product>> ListFamilyProductsAsync(CancellationToken cancellationToken)
    {
        var familyHandle = _settings.ProductFamilyHandle!.Trim();

        try
        {
            // The provider accepts either the family id or its handle prefixed with "handle:".
            return await ListProductsAsync("handle:" + familyHandle, cancellationToken);
        }
        catch (SubscriptionBillingException ex) when (ex.ProviderStatusCode == HttpStatusCode.NotFound)
        {
            // The handle form is percent-encoded into the path by the SDK. If a site rejects that,
            // fall back once to resolving the numeric id, rather than reporting an outage.
            _logger.LogWarning(
                "Listing products by family handle '{FamilyHandle}' returned 404; retrying once with the numeric family id.",
                familyHandle);

            var familyId = await ResolveProductFamilyIdAsync(familyHandle, cancellationToken);
            return await ListProductsAsync(familyId.ToString(CultureInfo.InvariantCulture), cancellationToken);
        }
    }

    private async Task<IReadOnlyList<Maxio.Product>> ListProductsAsync(string productFamilyId, CancellationToken cancellationToken)
    {
        const string Operation = "list the subscription plans";
        var products = new List<Maxio.Product>();

        for (var page = 1; page <= MaxPlanPages; page++)
        {
            IReadOnlyList<Maxio.ProductResponse> batch;

            try
            {
                batch = await _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: productFamilyId,
                    dateField: null,
                    filter: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    includeArchived: false,
                    include: null,
                    page: page,
                    perPage: PlanPageSize,
                    ct: cancellationToken);
            }
            catch (SdkException<MaxioErrors.ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetString(out var notFoundMessage))
                {
                    _logger.LogWarning("Billing provider reported the product family as not found: {ProviderMessage}", notFoundMessage);

                    throw new SubscriptionBillingException(
                        "The configured billing product family could not be found.",
                        HttpStatusCode.ServiceUnavailable,
                        providerStatusCode: HttpStatusCode.NotFound,
                        innerException: ex);
                }

                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw Translate(Operation, raw, ex);
                }

                throw new SubscriptionBillingException(
                    "The billing provider rejected the request for subscription plans.",
                    HttpStatusCode.BadGateway,
                    innerException: ex);
            }
            catch (JsonException ex)
            {
                throw TranslateUnreadableResponse(Operation, ex);
            }
            catch (HttpRequestException ex)
            {
                throw TranslateTransportFailure(Operation, ex);
            }

            foreach (var item in batch)
            {
                products.Add(item.Product);
            }

            if (batch.Count < PlanPageSize)
            {
                return products;
            }
        }

        _logger.LogWarning(
            "Stopped listing subscription plans after {MaxPlanPages} pages of {PlanPageSize}; the plan list may be truncated.",
            MaxPlanPages,
            PlanPageSize);

        return products;
    }

    private async Task<int> ResolveProductFamilyIdAsync(string familyHandle, CancellationToken cancellationToken)
    {
        const string Operation = "resolve the billing product family";
        IReadOnlyList<Maxio.ProductFamilyResponse> families;

        try
        {
            families = await _client.ProductFamilies.ListProductFamilies(
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                ct: cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(Operation, ex.Error, ex);
        }
        catch (JsonException ex)
        {
            throw TranslateUnreadableResponse(Operation, ex);
        }
        catch (HttpRequestException ex)
        {
            throw TranslateTransportFailure(Operation, ex);
        }

        var match = families
            .Select(response => response.ProductFamily)
            .FirstOrDefault(family => family is not null
                && string.Equals(family.Handle, familyHandle, StringComparison.OrdinalIgnoreCase));

        if (match?.Id is null)
        {
            throw new SubscriptionBillingException(
                "The configured billing product family could not be found.",
                HttpStatusCode.ServiceUnavailable,
                providerStatusCode: HttpStatusCode.NotFound);
        }

        return match.Id.Value;
    }

    private async Task<Maxio.Customer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        const string Operation = "look up the billing customer";

        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference, ct: cancellationToken);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            // A genuine miss - the only signal treated as "this shopper has no billing customer".
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(Operation, ex.Error, ex);
        }
        catch (JsonException ex)
        {
            // "I could not read the answer" is not "the provider said no". Mapping this onto a miss
            // would turn a corrupt response into a spurious customer create.
            throw TranslateUnreadableResponse(Operation, ex);
        }
        catch (HttpRequestException ex)
        {
            throw TranslateTransportFailure(Operation, ex);
        }
    }

    private async Task<Maxio.Customer> CreateCustomerAsync(Subscriber subscriber, CancellationToken cancellationToken)
    {
        const string Operation = "create the billing customer";

        var body = new Maxio.CreateCustomerRequest
        {
            Customer = new Maxio.CreateCustomer
            {
                FirstName = subscriber.FirstName,
                LastName = subscriber.LastName,
                Email = subscriber.Email,
                Reference = subscriber.Reference
            }
        };

        try
        {
            using (MaxioWriteScope.Begin())
            {
                var response = await _client.Customers.CreateCustomer(body, ct: cancellationToken);
                return response.Customer;
            }
        }
        catch (SdkException<MaxioErrors.CreateCustomerError> ex)
        {
            throw TranslateCreateCustomerError(ex);
        }
        catch (JsonException ex)
        {
            // Either a 2xx body that could not be read, or an error body that did not match the
            // generated shape - in which case the status was destroyed with it. Both mean the
            // outcome is unknown; the caller re-reads by reference before failing.
            _logger.LogError(ex, "Could not read the billing provider's answer while trying to {Operation}.", Operation);

            throw new SubscriptionBillingException(
                "The billing provider's answer to the customer creation request could not be processed.",
                HttpStatusCode.BadGateway,
                innerException: ex);
        }
        catch (MaxioWriteResendBlockedException ex)
        {
            throw new SubscriptionBillingException(
                "The billing customer creation could not be confirmed.",
                HttpStatusCode.ServiceUnavailable,
                innerException: ex);
        }
        catch (HttpRequestException ex)
        {
            throw TranslateTransportFailure(Operation, ex);
        }
    }

    private async Task<IReadOnlyList<Maxio.Subscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        const string Operation = "list the subscriptions";
        IReadOnlyList<Maxio.SubscriptionResponse> responses;

        try
        {
            responses = await _client.Customers.ListCustomerSubscriptions(customerId, ct: cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(Operation, ex.Error, ex);
        }
        catch (JsonException ex)
        {
            throw TranslateUnreadableResponse(Operation, ex);
        }
        catch (HttpRequestException ex)
        {
            throw TranslateTransportFailure(Operation, ex);
        }

        var subscriptions = new List<Maxio.Subscription>(responses.Count);
        foreach (var response in responses)
        {
            if (response.Subscription is not null)
            {
                subscriptions.Add(response.Subscription);
            }
        }

        return subscriptions;
    }

    private async Task<CustomerSubscription> CreateSubscriptionAsync(
        Subscriber subscriber,
        int customerId,
        string planHandle,
        CancellationToken cancellationToken)
    {
        const string Operation = "create the subscription";

        var body = new Maxio.CreateSubscriptionRequest
        {
            Subscription = new Maxio.CreateSubscription
            {
                ProductHandle = planHandle,
                CustomerId = customerId,
                Reference = subscriber.SubscriptionReference(planHandle),

                // This API enrolls a shopper without capturing a payment method, so the collection
                // method is stated explicitly rather than left to the site default. Under the usual
                // "automatic" default the provider tries to settle the first period immediately and
                // rejects the signup with "no payment method was on file"; remittance invoices the
                // shopper instead, which is what a signup with no card on file has to do. A
                // card-based flow would set this to automatic and attach a payment profile first.
                PaymentCollectionMethod = MaxioEnums.CollectionMethod.Remittance
            }
        };

        Maxio.SubscriptionResponse response;

        try
        {
            using (MaxioWriteScope.Begin())
            {
                response = await _client.Subscriptions.CreateSubscription(body, ct: cancellationToken);
            }
        }
        catch (SdkException<MaxioErrors.CreateSubscriptionError> ex)
        {
            throw TranslateCreateSubscriptionError(ex);
        }
        catch (Exception ex) when (ex is MaxioWriteResendBlockedException or HttpRequestException or JsonException)
        {
            return await ReconcileAfterUncertainCreateAsync(customerId, planHandle, ex, cancellationToken);
        }

        if (response.Subscription is null)
        {
            _logger.LogError("The billing provider accepted a subscription for customer {CustomerId} but returned no subscription body.", customerId);

            throw new SubscriptionBillingException(
                $"The billing provider accepted the request to {Operation} but did not return it.",
                HttpStatusCode.BadGateway);
        }

        var created = MapSubscription(response.Subscription);

        _logger.LogInformation(
            "Created subscription {SubscriptionId} in state {State} on plan {PlanHandle} for customer {CustomerId}.",
            created.Id,
            created.State,
            created.PlanHandle,
            customerId);

        return created;
    }

    // ------------------------------------------------------------- mapping

    private static SubscriptionPlan MapPlan(Maxio.Product product) => new SubscriptionPlan(
        handle: product.Handle!,
        name: string.IsNullOrWhiteSpace(product.Name) ? product.Handle! : product.Name!,
        description: product.Description,
        priceInCents: product.PriceInCents ?? 0L,
        interval: product.Interval ?? 0,
        intervalUnit: product.IntervalUnit?.Value ?? string.Empty,
        productFamilyHandle: product.ProductFamily?.Handle);

    private static CustomerSubscription MapSubscription(Maxio.Subscription subscription)
    {
        var product = subscription.Product;
        var state = subscription.State?.Value ?? string.Empty;

        return new CustomerSubscription(
            id: subscription.Id ?? 0,
            state: state,
            isLive: !TerminalStates.Contains(state),
            planHandle: product?.Handle ?? string.Empty,
            planName: string.IsNullOrWhiteSpace(product?.Name) ? product?.Handle ?? string.Empty : product!.Name!,
            priceInCents: subscription.ProductPriceInCents ?? product?.PriceInCents ?? 0L,
            currency: subscription.Currency,
            interval: product?.Interval ?? 0,
            intervalUnit: product?.IntervalUnit?.Value ?? string.Empty,
            nextBillingDate: subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
            currentPeriodEndsAt: subscription.CurrentPeriodEndsAt,
            createdAt: subscription.CreatedAt,
            customerId: subscription.Customer?.Id ?? 0,
            customerReference: subscription.Customer?.Reference);
    }

    private static int RequireCustomerId(Maxio.Customer customer)
    {
        if (customer.Id is null)
        {
            throw new SubscriptionBillingException(
                "The billing provider returned a customer without an identifier.",
                HttpStatusCode.BadGateway);
        }

        return customer.Id.Value;
    }

    // ------------------------------------------------------- error boundary

    private async Task<T> WithBudgetAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        EnsureConfigured();

        // One home for the whole-call bound, linked to the caller's own cancellation so a
        // disconnected client also stops the outbound work.
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(CallBudget);

        try
        {
            return await call(budget.Token);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "A billing call exceeded the {CallBudgetSeconds}s budget.", CallBudget.TotalSeconds);

            throw new SubscriptionBillingException(
                "The billing provider did not respond within the allowed time.",
                HttpStatusCode.GatewayTimeout,
                innerException: ex);
        }
    }

    private void EnsureConfigured()
    {
        var missing = _settings.MissingSettings();
        if (missing.Count == 0)
        {
            return;
        }

        throw new SubscriptionBillingException(
            "Subscription billing is not configured on this server.",
            HttpStatusCode.ServiceUnavailable,
            details: missing.Select(key => "Missing configuration value '" + key + "'.").ToList());
    }

    private SubscriptionBillingException TranslateCreateCustomerError(SdkException<MaxioErrors.CreateCustomerError> ex)
    {
        if (ex.Error.TryGetCustomerErrorResponse1(out var payload))
        {
            // The generated 422 payload for this operation disagrees in kind with the sibling one
            // used elsewhere, so read it best-effort and fall back to the generic message rather
            // than surfacing an empty error list.
            var details = new List<string>();
            var errors = payload?.Errors;

            if (errors?.PerPage is not null)
            {
                details.AddRange(errors.PerPage);
            }

            if (errors?.PricePoint is not null)
            {
                details.AddRange(errors.PricePoint);
            }

            return new SubscriptionBillingException(
                "The billing provider rejected the customer details.",
                HttpStatusCode.UnprocessableEntity,
                details: details.Count == 0 ? null : details,
                providerStatusCode: HttpStatusCode.UnprocessableEntity,
                innerException: ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return Translate("create the billing customer", raw, ex);
        }

        return new SubscriptionBillingException(
            "The billing provider rejected the request to create the billing customer.",
            HttpStatusCode.BadGateway,
            innerException: ex);
    }

    private SubscriptionBillingException TranslateCreateSubscriptionError(SdkException<MaxioErrors.CreateSubscriptionError> ex)
    {
        if (ex.Error.TryGetErrorListResponse1(out var payload))
        {
            var details = payload?.Errors?.ToList();

            _logger.LogWarning("The billing provider rejected a subscription request: {ProviderErrors}", string.Join("; ", details ?? new List<string>()));

            return new SubscriptionBillingException(
                "The billing provider rejected the subscription request.",
                HttpStatusCode.UnprocessableEntity,
                details: details,
                providerStatusCode: HttpStatusCode.UnprocessableEntity,
                innerException: ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return Translate("create the subscription", raw, ex);
        }

        return new SubscriptionBillingException(
            "The billing provider rejected the subscription request.",
            HttpStatusCode.BadGateway,
            innerException: ex);
    }

    private SubscriptionBillingException Translate(string operation, RawError raw, Exception inner)
    {
        var providerStatus = raw.StatusCode;
        var status = MapProviderStatus(providerStatus);

        _logger.LogError(
            inner,
            "The billing provider returned {ProviderStatus} while trying to {Operation}.",
            (int)providerStatus,
            operation);

        return new SubscriptionBillingException(
            BuildMessage(operation, status),
            status,
            providerStatusCode: providerStatus,
            innerException: inner);
    }

    private SubscriptionBillingException TranslateUnreadableResponse(string operation, JsonException ex)
    {
        _logger.LogError(ex, "Could not read the billing provider's response while trying to {Operation}.", operation);

        return new SubscriptionBillingException(
            $"The billing provider returned a response that could not be processed while trying to {operation}.",
            HttpStatusCode.BadGateway,
            innerException: ex);
    }

    private SubscriptionBillingException TranslateTransportFailure(string operation, Exception ex)
    {
        _logger.LogError(ex, "Could not reach the billing provider while trying to {Operation}.", operation);

        return new SubscriptionBillingException(
            $"The billing provider could not be reached while trying to {operation}.",
            HttpStatusCode.ServiceUnavailable,
            innerException: ex);
    }

    /// <summary>
    /// Maps a provider status onto the status this API answers with. A provider rejection the
    /// caller can act on stays a 4xx; a credential or availability problem, which is ours and not
    /// theirs, becomes a 5xx.
    /// </summary>
    private static HttpStatusCode MapProviderStatus(HttpStatusCode providerStatus)
    {
        var code = (int)providerStatus;

        return providerStatus switch
        {
            HttpStatusCode.Unauthorized => HttpStatusCode.BadGateway,
            HttpStatusCode.Forbidden => HttpStatusCode.BadGateway,
            HttpStatusCode.NotFound => HttpStatusCode.NotFound,
            HttpStatusCode.Conflict => HttpStatusCode.Conflict,
            HttpStatusCode.UnprocessableEntity => HttpStatusCode.UnprocessableEntity,
            HttpStatusCode.RequestTimeout => HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.TooManyRequests => HttpStatusCode.ServiceUnavailable,
            _ when code >= 500 => HttpStatusCode.ServiceUnavailable,
            _ when code >= 400 => HttpStatusCode.BadRequest,
            _ => HttpStatusCode.BadGateway
        };
    }

    private static string BuildMessage(string operation, HttpStatusCode status) => status switch
    {
        HttpStatusCode.NotFound => $"The billing provider could not find what was needed to {operation}.",
        HttpStatusCode.Conflict => $"The billing provider reported a conflict while trying to {operation}.",
        HttpStatusCode.UnprocessableEntity => $"The billing provider rejected the request to {operation}.",
        HttpStatusCode.BadRequest => $"The billing provider rejected the request to {operation}.",
        HttpStatusCode.ServiceUnavailable => $"The billing provider is currently unavailable, so it was not possible to {operation}.",
        _ => $"The billing provider could not be used to {operation}."
    };
}
