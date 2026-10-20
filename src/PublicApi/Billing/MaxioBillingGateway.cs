using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>Time budgets and paging bounds of <see cref="MaxioBillingGateway"/>.</summary>
public sealed class MaxioGatewayLimits
{
    /// <summary>Total budget for every SDK call made while serving one inbound request (one gateway scope).</summary>
    public TimeSpan TotalBudget { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Fresh budget for the re-read that settles a write whose outcome is unknown.</summary>
    public TimeSpan SettleBudget { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Page size for the plan list (the provider caps <c>per_page</c> at 200).</summary>
    public int PlansPerPage { get; init; } = 200;

    /// <summary>Page cap for the plan list: the read stops here and reports <see cref="PlanCatalog.Truncated"/>.</summary>
    public int MaxPlanPages { get; init; } = 5;

    /// <summary>Clock-skew allowance for the created-at fallback match while settling a subscription write.</summary>
    public TimeSpan SettleMatchSkew { get; init; } = TimeSpan.FromMinutes(2);
}

/// <summary>
/// The one class that fronts the Maxio Advanced Billing SDK. It is registered per request scope so that a
/// single <see cref="MaxioGatewayLimits.TotalBudget"/> bounds every SDK call made for one inbound request.
/// </summary>
public sealed class MaxioBillingGateway : IMaxioBillingGateway, IDisposable
{
    private const string CreateCustomerOp = "CreateCustomer";
    private const string CreateSubscriptionOp = "CreateSubscription";

    /// <summary>
    /// Collection method sent on every CreateSubscription. This flow captures no card, so the default
    /// (automatic) collection can never succeed: the sandbox refused it with 422 "No payment method was on
    /// file for the ... balance". Remittance bills the customer by invoice and needs no payment method.
    /// </summary>
    private static readonly MaxioAdvancedBilling.Models.Enums.CollectionMethod SubscriptionCollectionMethod =
        MaxioAdvancedBilling.Models.Enums.CollectionMethod.Remittance;

    private readonly MaxioAdvancedBilling.MaxioAdvancedBillingClient _client;
    private readonly string _productFamilyHandle;
    private readonly MaxioGatewayLimits _limits;
    private readonly ILogger<MaxioBillingGateway> _logger;
    private CancellationTokenSource? _budget;

    public MaxioBillingGateway(
        MaxioAdvancedBilling.MaxioAdvancedBillingClient client,
        IOptions<MaxioSettings> settings,
        MaxioGatewayLimits limits,
        ILogger<MaxioBillingGateway> logger)
    {
        _client = client;
        _productFamilyHandle = settings.Value.ProductFamilyHandle ?? string.Empty;
        _limits = limits;
        _logger = logger;
    }

    public void Dispose() => _budget?.Dispose();

    // ---------------------------------------------------------------- reads

    public async Task<PlanCatalog> ListPlansAsync(CancellationToken cancellationToken)
    {
        var plans = new List<BillingPlan>();
        var truncated = false;
        try
        {
            for (var page = 1; ; page++)
            {
                var request = new MaxioAdvancedBilling.Requests.ProductFamilies.ListProductsForProductFamilyRequest
                {
                    ProductFamilyId = "handle:" + _productFamilyHandle,
                    Page = page,
                    PerPage = _limits.PlansPerPage,
                };
                var items = await Bounded(t => _client.ProductFamilies.ListProductsForProductFamily(request, cancellationToken: t), cancellationToken);

                foreach (var item in items)
                {
                    var plan = ToPlan(item?.Product);
                    if (plan != null)
                    {
                        plans.Add(plan);
                    }
                }

                if (items.Count < _limits.PlansPerPage)
                {
                    break; // a short page is the last page
                }
                if (page >= _limits.MaxPlanPages)
                {
                    truncated = true; // the cap, not the provider, stopped the read
                    _logger.LogWarning("Maxio plan list for family {ProductFamilyHandle} stopped at the {MaxPages}-page cap; result is truncated.",
                        _productFamilyHandle, _limits.MaxPlanPages);
                    break;
                }
            }
        }
        catch (ApiException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out _))
            {
                _logger.LogError("Maxio product family {ProductFamilyHandle} was not found (HTTP {Status}). Check Maxio:ProductFamilyHandle.",
                    _productFamilyHandle, (int)ex.StatusCode);
                throw new BillingProviderException(BillingFailureKind.Unavailable,
                    "The configured subscription plan family was not found at the billing provider.", (int)ex.StatusCode, innerException: ex);
            }
            if (ex.Error.TryGetRawError(out RawError raw))
            {
                throw TranslateRead(ex, "ListProductsForProductFamily", (int)raw.StatusCode, cancellationToken);
            }
            throw TranslateRead(ex, "ListProductsForProductFamily", (int)ex.StatusCode, cancellationToken);
        }
        catch (Exception ex) when (IsSdkOrBudgetFailure(ex, cancellationToken))
        {
            throw TranslateRead(ex, "ListProductsForProductFamily", null, cancellationToken);
        }

        return new PlanCatalog(plans, truncated);
    }

    public async Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            return await Bounded(t => ReadCustomerByReferenceCore(reference, t), cancellationToken);
        }
        catch (ApiException<RawError> ex)
        {
            throw TranslateRead(ex, "ReadCustomerByReference", (int)ex.StatusCode, cancellationToken);
        }
        catch (Exception ex) when (IsSdkOrBudgetFailure(ex, cancellationToken))
        {
            throw TranslateRead(ex, "ReadCustomerByReference", null, cancellationToken);
        }
    }

    public async Task<BillingSubscription?> FindSubscriptionAsync(SubscriptionLookup lookup, CancellationToken cancellationToken)
    {
        try
        {
            return await Bounded(t => LookupSubscriptionCore(lookup, t), cancellationToken);
        }
        catch (ApiException<MaxioAdvancedBilling.Errors.FindSubscriptionError> ex)
        {
            throw TranslateRead(ex, "FindSubscription", (int)ex.StatusCode, cancellationToken);
        }
        catch (ApiException<RawError> ex)
        {
            throw TranslateRead(ex, "ListCustomerSubscriptions", (int)ex.StatusCode, cancellationToken);
        }
        catch (Exception ex) when (IsSdkOrBudgetFailure(ex, cancellationToken))
        {
            throw TranslateRead(ex, "FindSubscription", null, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<BillingSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        try
        {
            return await Bounded(t => ListCustomerSubscriptionsCore(customerId, t), cancellationToken);
        }
        catch (ApiException<RawError> ex) when ((int)ex.StatusCode == 404)
        {
            return Array.Empty<BillingSubscription>();
        }
        catch (ApiException<RawError> ex)
        {
            throw TranslateRead(ex, "ListCustomerSubscriptions", (int)ex.StatusCode, cancellationToken);
        }
        catch (Exception ex) when (IsSdkOrBudgetFailure(ex, cancellationToken))
        {
            throw TranslateRead(ex, "ListCustomerSubscriptions", null, cancellationToken);
        }
    }

    // ---------------------------------------------------------------- writes

    public async Task<WriteOutcome<BillingCustomer>> CreateCustomerAsync(NewBillingCustomer customer, CancellationToken cancellationToken)
    {
        var request = new MaxioAdvancedBilling.Requests.Customers.CreateCustomerOperationRequest
        {
            Body = new MaxioAdvancedBilling.Models.CreateCustomerRequest
            {
                Customer = new MaxioAdvancedBilling.Models.CreateCustomer
                {
                    FirstName = customer.FirstName,
                    LastName = customer.LastName,
                    Email = customer.Email,
                    Reference = customer.Reference,
                },
            },
        };

        try
        {
            var response = await Bounded(t => _client.Customers.CreateCustomer(request, cancellationToken: t), cancellationToken);
            var created = ToCustomer(response.Customer);
            if (created == null)
            {
                // 2xx without a usable id: the customer may exist; settle by reference.
                return await SettleCustomerAsync(customer.Reference, "2xx response without a customer id");
            }
            _logger.LogInformation("Maxio {Operation} created customer {MaxioCustomerId} for {CustomerReference}.",
                CreateCustomerOp, created.Id, customer.Reference);
            return WriteOutcome<BillingCustomer>.Created(created);
        }
        catch (ApiException<MaxioAdvancedBilling.Errors.CreateCustomerError> ex)
        {
            var status = (int)ex.StatusCode;
            var messages = new List<string>();
            if (ex.Error.TryGetCustomerErrorResponse1(out var body))
            {
                messages.AddRange(ReadCustomerErrors(body.Errors));
            }
            else if (ex.Error.TryGetRawError(out RawError raw))
            {
                status = (int)raw.StatusCode;
            }
            return await HandleCustomerWriteErrorAsync(customer.Reference, status, messages, ex, cancellationToken);
        }
        catch (ResponseDeserializationException ex)
        {
            return await HandleCustomerWriteErrorAsync(customer.Reference, (int)ex.StatusCode, new List<string>(), ex, cancellationToken);
        }
        catch (AuthSchemeException ex)
        {
            // Nothing was sent: our credential could not be applied.
            throw Unavailable(CreateCustomerOp, "credentials could not be applied", null, ex);
        }
        catch (SdkConnectionException ex)
        {
            // Includes SdkTimeoutException. The POST may still have landed.
            return await SettleCustomerAsync(customer.Reference, ex.GetType().Name);
        }
        catch (OperationCanceledException)
        {
            // Our budget or the caller's disconnect cancelled the POST mid-flight. It may still have landed.
            return await SettleCustomerAsync(customer.Reference, BudgetExpired(cancellationToken) ? "total budget exhausted" : "request aborted");
        }
    }

    public async Task<WriteOutcome<BillingSubscription>> CreateSubscriptionAsync(NewBillingSubscription subscription, CancellationToken cancellationToken)
    {
        var request = new MaxioAdvancedBilling.Requests.Subscriptions.CreateSubscriptionOperationRequest
        {
            Body = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
            {
                Subscription = new MaxioAdvancedBilling.Models.CreateSubscription
                {
                    ProductHandle = subscription.PlanHandle,
                    CustomerId = subscription.CustomerId,
                    Reference = subscription.Reference,
                    PaymentCollectionMethod = SubscriptionCollectionMethod,
                },
            },
        };
        var lookup = new SubscriptionLookup(subscription.Reference, subscription.CustomerId, subscription.PlanHandle, subscription.AttemptStartedAt);

        try
        {
            var response = await Bounded(t => _client.Subscriptions.CreateSubscription(request, cancellationToken: t), cancellationToken);
            var created = ToSubscription(response.Subscription);
            if (created == null)
            {
                return await SettleSubscriptionAsync(lookup, "2xx response without a subscription id");
            }
            _logger.LogInformation("Maxio {Operation} created subscription {MaxioSubscriptionId} ({State}) for {SubscriptionReference}.",
                CreateSubscriptionOp, created.Id, created.State, subscription.Reference);
            return WriteOutcome<BillingSubscription>.Created(created);
        }
        catch (ApiException<MaxioAdvancedBilling.Errors.CreateSubscriptionError> ex)
        {
            var status = (int)ex.StatusCode;
            IReadOnlyList<string> messages = Array.Empty<string>();
            if (ex.Error.TryGetErrorListResponse1(out var body))
            {
                messages = body.Errors?.Where(m => !string.IsNullOrWhiteSpace(m)).ToList() ?? new List<string>();
            }
            else if (ex.Error.TryGetRawError(out RawError raw))
            {
                status = (int)raw.StatusCode;
            }
            return await HandleSubscriptionWriteErrorAsync(lookup, status, messages, ex, cancellationToken);
        }
        catch (ResponseDeserializationException ex)
        {
            return await HandleSubscriptionWriteErrorAsync(lookup, (int)ex.StatusCode, Array.Empty<string>(), ex, cancellationToken);
        }
        catch (AuthSchemeException ex)
        {
            throw Unavailable(CreateSubscriptionOp, "credentials could not be applied", null, ex);
        }
        catch (SdkConnectionException ex)
        {
            return await SettleSubscriptionAsync(lookup, ex.GetType().Name);
        }
        catch (OperationCanceledException)
        {
            return await SettleSubscriptionAsync(lookup, BudgetExpired(cancellationToken) ? "total budget exhausted" : "request aborted");
        }
    }

    // ---------------------------------------------------------------- write error handling + settlement

    private async Task<WriteOutcome<BillingCustomer>> HandleCustomerWriteErrorAsync(
        string reference, int status, IReadOnlyList<string> messages, ApiException ex, CancellationToken cancellationToken)
    {
        if (status is >= 200 and < 300 || status >= 500)
        {
            // An unreadable success body or a provider fault on a POST: the customer may exist.
            return await SettleCustomerAsync(reference, $"HTTP {status} ({ex.GetType().Name})");
        }
        ThrowIfOurFault(CreateCustomerOp, status, ex);

        // The provider refused THIS write (e.g. 422). A duplicate reference is refused the same way, so look
        // the reference up before calling it a refusal. Provider message text is never parsed.
        var existing = await TryReadCustomerForSettlementAsync(reference);
        if (existing != null)
        {
            _logger.LogInformation("Maxio {Operation} refused with HTTP {Status} but customer {MaxioCustomerId} already exists for {CustomerReference}.",
                CreateCustomerOp, status, existing.Id, reference);
            return WriteOutcome<BillingCustomer>.AlreadyExisted(existing);
        }
        // Customer error text can echo the e-mail address we sent, so only its count is logged.
        _logger.LogWarning("Maxio {Operation} refused with HTTP {Status} for {CustomerReference} ({MessageCount} provider messages).",
            CreateCustomerOp, status, reference, messages.Count);
        return WriteOutcome<BillingCustomer>.Refused(messages, status);
    }

    private async Task<WriteOutcome<BillingSubscription>> HandleSubscriptionWriteErrorAsync(
        SubscriptionLookup lookup, int status, IReadOnlyList<string> messages, ApiException ex, CancellationToken cancellationToken)
    {
        if (status is >= 200 and < 300 || status >= 500)
        {
            return await SettleSubscriptionAsync(lookup, $"HTTP {status} ({ex.GetType().Name})");
        }
        ThrowIfOurFault(CreateSubscriptionOp, status, ex);

        var existing = await TryLookupSubscriptionForSettlementAsync(lookup with { UnsettledAttemptStartedAt = null });
        if (existing != null)
        {
            _logger.LogInformation("Maxio {Operation} refused with HTTP {Status} but subscription {MaxioSubscriptionId} already exists for {SubscriptionReference}.",
                CreateSubscriptionOp, status, existing.Id, lookup.Reference);
            return WriteOutcome<BillingSubscription>.AlreadyExisted(existing);
        }
        _logger.LogWarning("Maxio {Operation} refused with HTTP {Status} for {SubscriptionReference}: {ProviderMessages}",
            CreateSubscriptionOp, status, lookup.Reference, string.Join(" | ", messages));
        return WriteOutcome<BillingSubscription>.Refused(messages, status);
    }

    private async Task<WriteOutcome<BillingCustomer>> SettleCustomerAsync(string reference, string cause)
    {
        _logger.LogWarning("Maxio {Operation} outcome unknown for {CustomerReference} ({Cause}); re-reading by reference.",
            CreateCustomerOp, reference, cause);
        var found = await TryReadCustomerForSettlementAsync(reference);
        if (found != null)
        {
            _logger.LogInformation("Maxio {Operation} settled: customer {MaxioCustomerId} exists for {CustomerReference}.",
                CreateCustomerOp, found.Id, reference);
            return WriteOutcome<BillingCustomer>.Created(found);
        }
        _logger.LogError("Maxio {Operation} outcome for {CustomerReference} could not be settled; recorded as unknown.",
            CreateCustomerOp, reference);
        return WriteOutcome<BillingCustomer>.Unknown();
    }

    private async Task<WriteOutcome<BillingSubscription>> SettleSubscriptionAsync(SubscriptionLookup lookup, string cause)
    {
        _logger.LogWarning("Maxio {Operation} outcome unknown for {SubscriptionReference} ({Cause}); re-reading by reference.",
            CreateSubscriptionOp, lookup.Reference, cause);
        var found = await TryLookupSubscriptionForSettlementAsync(lookup);
        if (found != null)
        {
            _logger.LogInformation("Maxio {Operation} settled: subscription {MaxioSubscriptionId} exists for {SubscriptionReference}.",
                CreateSubscriptionOp, found.Id, lookup.Reference);
            return WriteOutcome<BillingSubscription>.Created(found);
        }
        _logger.LogError("Maxio {Operation} outcome for {SubscriptionReference} could not be settled; recorded as unknown.",
            CreateSubscriptionOp, lookup.Reference);
        return WriteOutcome<BillingSubscription>.Unknown();
    }

    /// <summary>Re-read on a FRESH budget (the request budget may be what fired). Any failure yields <c>null</c>.</summary>
    private async Task<BillingCustomer?> TryReadCustomerForSettlementAsync(string reference)
    {
        using var settle = new CancellationTokenSource(_limits.SettleBudget);
        try
        {
            return await ReadCustomerByReferenceCore(reference, settle.Token);
        }
        catch (Exception ex) when (ex is SdkException || ex is OperationCanceledException || ex is BillingProviderException)
        {
            _logger.LogWarning("Maxio ReadCustomerByReference re-read failed for {CustomerReference} ({Failure}).", reference, ex.GetType().Name);
            return null;
        }
    }

    private async Task<BillingSubscription?> TryLookupSubscriptionForSettlementAsync(SubscriptionLookup lookup)
    {
        using var settle = new CancellationTokenSource(_limits.SettleBudget);
        try
        {
            return await LookupSubscriptionCore(lookup, settle.Token);
        }
        catch (Exception ex) when (ex is SdkException || ex is OperationCanceledException || ex is BillingProviderException)
        {
            _logger.LogWarning("Maxio subscription re-read failed for {SubscriptionReference} ({Failure}).", lookup.Reference, ex.GetType().Name);
            return null;
        }
    }

    // ---------------------------------------------------------------- raw SDK calls (no translation)

    private async Task<BillingCustomer?> ReadCustomerByReferenceCore(string reference, CancellationToken token)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(
                new MaxioAdvancedBilling.Requests.Customers.ReadCustomerByReferenceRequest { Reference = reference },
                cancellationToken: token);
            var customer = ToCustomer(response.Customer);
            if (customer == null)
            {
                throw new BillingProviderException(BillingFailureKind.Unavailable,
                    "The billing provider returned a customer without an id.");
            }
            return customer;
        }
        catch (ApiException<RawError> ex) when ((int)ex.StatusCode == 404)
        {
            return null; // the provider holds no customer with this reference
        }
    }

    private async Task<BillingSubscription?> LookupSubscriptionCore(SubscriptionLookup lookup, CancellationToken token)
    {
        try
        {
            var response = await _client.Subscriptions.FindSubscription(
                new MaxioAdvancedBilling.Requests.Subscriptions.FindSubscriptionRequest { Reference = lookup.Reference },
                cancellationToken: token);
            var found = ToSubscription(response.Subscription);
            if (found != null)
            {
                return found;
            }
        }
        catch (ApiException<MaxioAdvancedBilling.Errors.FindSubscriptionError> ex) when (ex.Error.TryGetNoContent(out _))
        {
            // 404: no subscription carries this reference; fall through to the customer's list.
        }

        // Whether the provider persists/indexes the subscription reference is UNVERIFIED: also scan the
        // caller's own customer's subscriptions.
        var all = await ListCustomerSubscriptionsCore(lookup.CustomerId, token);
        var byReference = all.FirstOrDefault(s => string.Equals(s.Reference, lookup.Reference, StringComparison.Ordinal));
        if (byReference != null || lookup.UnsettledAttemptStartedAt is null)
        {
            return byReference;
        }

        var since = lookup.UnsettledAttemptStartedAt.Value - _limits.SettleMatchSkew;
        return all
            .Where(s => string.IsNullOrEmpty(s.Reference)
                        && string.Equals(s.PlanHandle, lookup.PlanHandle, StringComparison.OrdinalIgnoreCase)
                        && s.CreatedAt >= since)
            .OrderBy(s => s.CreatedAt)
            .FirstOrDefault();
    }

    private async Task<IReadOnlyList<BillingSubscription>> ListCustomerSubscriptionsCore(int customerId, CancellationToken token)
    {
        var items = await _client.Customers.ListCustomerSubscriptions(
            new MaxioAdvancedBilling.Requests.Customers.ListCustomerSubscriptionsRequest { CustomerId = customerId },
            cancellationToken: token);
        var result = new List<BillingSubscription>(items.Count);
        foreach (var item in items)
        {
            var mapped = ToSubscription(item?.Subscription);
            if (mapped != null)
            {
                result.Add(mapped);
            }
        }
        return result;
    }

    // ---------------------------------------------------------------- budget

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        _budget ??= new CancellationTokenSource(_limits.TotalBudget);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_budget.Token, cancellationToken);
        return await call(linked.Token);
    }

    private bool BudgetExpired(CancellationToken callerToken) =>
        !callerToken.IsCancellationRequested && _budget?.IsCancellationRequested == true;

    private bool IsSdkOrBudgetFailure(Exception ex, CancellationToken callerToken) =>
        ex is SdkException || (ex is OperationCanceledException && BudgetExpired(callerToken));

    // ---------------------------------------------------------------- translation (one ladder)

    private BillingProviderException TranslateRead(Exception ex, string operation, int? status, CancellationToken callerToken)
    {
        switch (ex)
        {
            case BillingProviderException bpe:
                return bpe;
            case ResponseDeserializationException rde:
                _logger.LogError("Maxio {Operation} returned HTTP {Status} with a body that could not be read.", operation, (int)rde.StatusCode);
                return new BillingProviderException(BillingFailureKind.Unavailable,
                    "The billing provider returned a response that could not be processed.", (int)rde.StatusCode, innerException: ex);
            case ApiException api:
                return FromStatus(operation, status ?? (int)api.StatusCode, ex);
            case SdkTimeoutException:
                _logger.LogError("Maxio {Operation} timed out.", operation);
                return new BillingProviderException(BillingFailureKind.Timeout,
                    "The billing provider did not answer in time.", innerException: ex);
            case SdkConnectionException:
                _logger.LogError("Maxio {Operation} could not reach the provider.", operation);
                return new BillingProviderException(BillingFailureKind.Unavailable,
                    "The billing provider is unreachable.", innerException: ex);
            case AuthSchemeException:
                return Unavailable(operation, "credentials could not be applied", null, ex);
            case OperationCanceledException when BudgetExpired(callerToken):
                _logger.LogError("Maxio {Operation} exceeded the total budget of {Budget}.", operation, _limits.TotalBudget);
                return new BillingProviderException(BillingFailureKind.Timeout,
                    "The billing provider did not answer in time.", innerException: ex);
            default:
                return new BillingProviderException(BillingFailureKind.Unavailable,
                    "The billing provider request failed.", status, innerException: ex);
        }
    }

    private BillingProviderException FromStatus(string operation, int status, Exception ex)
    {
        if (status == 429)
        {
            _logger.LogWarning("Maxio {Operation} was throttled (HTTP 429).", operation);
            return new BillingProviderException(BillingFailureKind.Throttled,
                "The billing provider is temporarily throttling requests.", status, innerException: ex);
        }
        return Unavailable(operation, $"HTTP {status}", status, ex);
    }

    /// <summary>Statuses on a write that mean OUR credentials/configuration are wrong, not the caller's data.</summary>
    private void ThrowIfOurFault(string operation, int status, Exception ex)
    {
        if (status is 401 or 403 or 404 or 405 or 429)
        {
            throw FromStatus(operation, status, ex);
        }
    }

    private BillingProviderException Unavailable(string operation, string detail, int? status, Exception ex)
    {
        _logger.LogError("Maxio {Operation} failed: {Detail}.", operation, detail);
        return new BillingProviderException(BillingFailureKind.Unavailable,
            status is 401 or 403
                ? "The billing provider rejected this application's credentials."
                : "The billing provider is currently unavailable.",
            status, innerException: ex);
    }

    // ---------------------------------------------------------------- mapping

    private static IEnumerable<string> ReadCustomerErrors(MaxioAdvancedBilling.Models.AnyOf.Errors1? errors)
    {
        if (errors == null)
        {
            yield break;
        }
        if (errors.TryGetCustomerError(out var customerError))
        {
            if (!string.IsNullOrWhiteSpace(customerError.Customer))
            {
                yield return customerError.Customer;
            }
        }
        else if (errors.TryGetListOfString(out var list))
        {
            foreach (var message in list)
            {
                if (!string.IsNullOrWhiteSpace(message))
                {
                    yield return message;
                }
            }
        }
    }

    private static BillingPlan? ToPlan(MaxioAdvancedBilling.Models.Product? product)
    {
        if (product == null || string.IsNullOrWhiteSpace(product.Handle) || product.ArchivedAt != null)
        {
            return null;
        }
        return new BillingPlan(
            product.Id,
            product.Handle,
            product.Name,
            product.Description,
            product.PriceInCents,
            product.Interval,
            product.IntervalUnit?.Value);
    }

    private static BillingCustomer? ToCustomer(MaxioAdvancedBilling.Models.Customer? customer) =>
        customer?.Id is int id ? new BillingCustomer(id, customer.Reference) : null;

    private static BillingSubscription? ToSubscription(MaxioAdvancedBilling.Models.Subscription? subscription)
    {
        if (subscription?.Id is not int id)
        {
            return null;
        }
        return new BillingSubscription(
            id,
            subscription.Reference,
            // Open enum: .Value is the raw wire string, also for values this SDK does not declare.
            subscription.State?.Value ?? "unknown",
            subscription.Product?.Handle,
            subscription.Product?.Name,
            subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents,
            subscription.Currency,
            subscription.NextAssessmentAt,
            subscription.CurrentPeriodEndsAt,
            subscription.CreatedAt,
            subscription.ActivatedAt,
            // Open enum: raw wire string ("remittance", "automatic", ...), null when the provider omits it.
            subscription.PaymentCollectionMethod?.Value);
    }
}
