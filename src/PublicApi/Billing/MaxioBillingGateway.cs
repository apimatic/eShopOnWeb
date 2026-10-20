using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.AnyOf;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Requests.Customers;
using MaxioAdvancedBilling.Requests.ProductFamilies;
using MaxioAdvancedBilling.Requests.Subscriptions;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// The one class that talks to the Maxio Advanced Billing SDK. Every call runs under a total per-call budget
/// (<see cref="MaxioCallPolicy.CallBudget"/>) linked to the caller's token, and every SDK failure is translated
/// into a caller-safe <see cref="BillingException"/>. The two writes settle an unknown outcome inside their own
/// catch by re-reading Maxio before they return.
/// </summary>
public class MaxioBillingGateway
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly MaxioCallPolicy _policy;
    private readonly ILogger<MaxioBillingGateway> _logger;

    public MaxioBillingGateway(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioSettings> settings,
        MaxioCallPolicy policy,
        ILogger<MaxioBillingGateway> logger)
    {
        _client = client;
        _settings = settings.Value;
        _policy = policy;
        _logger = logger;
    }

    // ---------------------------------------------------------------- plans

    /// <summary>
    /// Lists the configured family's non-archived products, walking pages until a short page or the page cap.
    /// The result says whether the cap cut it short (<see cref="PlanCatalog.Truncated"/>).
    /// </summary>
    public async Task<PlanCatalog> ListPlansAsync(CancellationToken cancellationToken)
    {
        const string operation = "ListProductsForProductFamily";
        var familyId = "handle:" + _settings.ProductFamilyHandle;
        var plans = new List<BillingPlan>();
        var truncated = false;

        for (var page = 1; ; page++)
        {
            IReadOnlyList<ProductResponse> items;
            try
            {
                var request = new ListProductsForProductFamilyRequest
                {
                    ProductFamilyId = familyId,
                    Page = page,
                    PerPage = _policy.PlanPageSize,
                    IncludeArchived = false,
                };
                items = await Bounded(
                    t => _client.ProductFamilies.ListProductsForProductFamily(request, cancellationToken: t),
                    cancellationToken);
            }
            catch (ApiException<ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetString(out _))
                {
                    // 404: the configured family does not exist on this site — a deployment fault, not the caller's.
                    _logger.LogError("Maxio {Operation}: configured product family was not found (status {Status}).", operation, (int)ex.StatusCode);
                    throw new BillingException(502, "Subscription plans are currently unavailable.", ex) { ProviderStatusCode = (int)ex.StatusCode };
                }
                if (ex.Error.TryGetRawError(out RawError _))
                {
                    throw FromProviderStatus(operation, ex.StatusCode, null, ex, isWrite: false);
                }
                throw FromProviderStatus(operation, ex.StatusCode, null, ex, isWrite: false);
            }
            catch (Exception ex) when (IsTransportOrShapeFailure(ex))
            {
                throw ReadFailure(operation, ex);
            }

            foreach (var product in items.Select(i => i?.Product))
            {
                if (product is null || string.IsNullOrWhiteSpace(product.Handle) || product.ArchivedAt is not null)
                {
                    continue;
                }
                plans.Add(new BillingPlan(
                    product.Handle!,
                    product.Name ?? product.Handle!,
                    product.Description,
                    product.PriceInCents,
                    product.Interval,
                    product.IntervalUnit?.Value,
                    product.RequireCreditCard));
            }

            if (items.Count < _policy.PlanPageSize)
            {
                break;
            }
            if (page >= _policy.MaxPlanPages)
            {
                truncated = true;
                _logger.LogWarning("Maxio {Operation}: stopped at the page cap ({MaxPages} pages); plan catalogue is truncated.", operation, _policy.MaxPlanPages);
                break;
            }
        }

        return new PlanCatalog(plans, truncated);
    }

    // ---------------------------------------------------------------- customers

    /// <summary>Looks a customer up by reference. Returns null only when Maxio answers 404.</summary>
    public async Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        const string operation = "ReadCustomerByReference";
        CustomerResponse response;
        try
        {
            response = await Bounded(
                t => _client.Customers.ReadCustomerByReference(new ReadCustomerByReferenceRequest { Reference = reference }, cancellationToken: t),
                cancellationToken);
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (ApiException<RawError> ex)
        {
            throw FromProviderStatus(operation, ex.StatusCode, null, ex, isWrite: false);
        }
        catch (Exception ex) when (IsTransportOrShapeFailure(ex))
        {
            throw ReadFailure(operation, ex);
        }

        if (response.Customer?.Id is int id)
        {
            return new BillingCustomer(id, response.Customer.Reference);
        }

        // An answer we cannot use is not an absence: never let it turn into a duplicate create.
        _logger.LogError("Maxio {Operation}: response carried no customer id.", operation);
        throw new BillingException(502, "The billing provider returned a response that could not be processed.");
    }

    /// <summary>Re-reads Maxio for a customer we may have created. Never throws for provider failures.</summary>
    public async Task<Reconciliation<BillingCustomer>> ReconcileCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var found = await FindCustomerByReferenceAsync(reference, cancellationToken);
            return new Reconciliation<BillingCustomer>(found, LookupSucceeded: true);
        }
        catch (BillingException ex)
        {
            _logger.LogWarning("Maxio customer reconciliation for reference {Reference} could not complete ({Status}).", reference, ex.ProviderStatusCode);
            return new Reconciliation<BillingCustomer>(null, LookupSucceeded: false);
        }
    }

    /// <summary>
    /// Creates a customer carrying <paramref name="reference"/>. A connection failure, timeout, 5xx or an
    /// unreadable 2xx is settled here by looking the reference up; if that cannot establish the outcome the
    /// result is <see cref="WriteResult{T}.OutcomeUnknown"/>. A definite refusal throws <see cref="BillingException"/>.
    /// </summary>
    public async Task<WriteResult<BillingCustomer>> CreateCustomerAsync(
        string reference, string email, string firstName, string lastName, CancellationToken cancellationToken)
    {
        const string operation = "CreateCustomer";
        try
        {
            var request = new CreateCustomerOperationRequest
            {
                Body = new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = firstName,
                        LastName = lastName,
                        Email = email,
                        Reference = reference,
                    }
                }
            };
            var response = await Bounded(t => _client.Customers.CreateCustomer(request, cancellationToken: t), cancellationToken);

            if (response.Customer?.Id is int id)
            {
                _logger.LogInformation("Maxio customer {CustomerId} created for reference {Reference}.", id, reference);
                return WriteResult<BillingCustomer>.Landed(new BillingCustomer(id, response.Customer.Reference));
            }
            _logger.LogError("Maxio {Operation}: 2xx response carried no customer id; settling by reference.", operation);
            return await SettleCustomerAsync(reference, cancellationToken);
        }
        catch (ApiException<CreateCustomerError> ex) when ((int)ex.StatusCode >= 500)
        {
            _logger.LogError("Maxio {Operation} answered {Status}; outcome unknown, settling by reference.", operation, (int)ex.StatusCode);
            return await SettleCustomerAsync(reference, cancellationToken);
        }
        catch (ApiException<CreateCustomerError> ex)
        {
            string? detail = null;
            if (ex.Error.TryGetCustomerErrorResponse1(out CustomerErrorResponse1 body))
            {
                detail = Describe(body.Errors);
            }
            else if (ex.Error.TryGetRawError(out RawError _))
            {
                detail = null;
            }

            if (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                // Maxio allows one customer per reference: a refusal may mean the customer already exists.
                var existing = await ReconcileCustomerAsync(reference, cancellationToken);
                if (existing.Found is not null)
                {
                    _logger.LogInformation("Maxio {Operation} refused reference {Reference}; existing customer {CustomerId} adopted.", operation, reference, existing.Found.Id);
                    return WriteResult<BillingCustomer>.Landed(existing.Found);
                }
            }
            throw FromProviderStatus(operation, ex.StatusCode, detail, ex, isWrite: true);
        }
        catch (ResponseDeserializationException ex) when (IsSuccessOrServerError(ex.StatusCode))
        {
            _logger.LogError("Maxio {Operation}: unreadable {Status} response; settling by reference.", operation, (int)ex.StatusCode);
            return await SettleCustomerAsync(reference, cancellationToken);
        }
        catch (ResponseDeserializationException ex)
        {
            throw FromProviderStatus(operation, ex.StatusCode, null, ex, isWrite: true);
        }
        catch (SdkConnectionException ex) // includes SdkTimeoutException: the write may still have landed
        {
            _logger.LogError("Maxio {Operation}: {Failure}; outcome unknown, settling by reference.", operation, ex.GetType().Name);
            return await SettleCustomerAsync(reference, cancellationToken);
        }
        catch (CallBudgetExceededException)
        {
            _logger.LogError("Maxio {Operation}: call budget {Budget} exceeded; outcome unknown, settling by reference.", operation, _policy.CallBudget);
            return await SettleCustomerAsync(reference, cancellationToken);
        }
        catch (AuthSchemeException ex)
        {
            throw ReadFailure(operation, ex);
        }
    }

    private async Task<WriteResult<BillingCustomer>> SettleCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        var reconciliation = await ReconcileCustomerAsync(reference, cancellationToken);
        if (reconciliation.Found is not null)
        {
            _logger.LogInformation("Maxio customer {CustomerId} found by reference {Reference} after an unknown outcome.", reconciliation.Found.Id, reference);
            return WriteResult<BillingCustomer>.Landed(reconciliation.Found);
        }
        _logger.LogWarning("Maxio customer for reference {Reference}: outcome still unknown (lookup succeeded: {LookupSucceeded}).", reference, reconciliation.LookupSucceeded);
        return WriteResult<BillingCustomer>.Unknown();
    }

    // ---------------------------------------------------------------- subscriptions

    /// <summary>Lists every subscription of a customer (the operation is not paged).</summary>
    public async Task<IReadOnlyList<BillingSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        const string operation = "ListCustomerSubscriptions";
        IReadOnlyList<SubscriptionResponse> items;
        try
        {
            items = await Bounded(
                t => _client.Customers.ListCustomerSubscriptions(new ListCustomerSubscriptionsRequest { CustomerId = customerId }, cancellationToken: t),
                cancellationToken);
        }
        catch (ApiException<RawError> ex)
        {
            throw FromProviderStatus(operation, ex.StatusCode, null, ex, isWrite: false);
        }
        catch (Exception ex) when (IsTransportOrShapeFailure(ex))
        {
            throw ReadFailure(operation, ex);
        }

        return items
            .Select(i => i?.Subscription)
            .Where(s => s?.Id is not null)
            .Select(s => Map(s!))
            .ToList();
    }

    /// <summary>Finds a subscription by its reference. Returns null only when Maxio answers 404.</summary>
    public async Task<BillingSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        const string operation = "FindSubscription";
        SubscriptionResponse response;
        try
        {
            response = await Bounded(
                t => _client.Subscriptions.FindSubscription(new FindSubscriptionRequest { Reference = reference }, cancellationToken: t),
                cancellationToken);
        }
        catch (ApiException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out RawError _))
            {
                return null;
            }
            if (ex.Error.TryGetRawError(out RawError _))
            {
                throw FromProviderStatus(operation, ex.StatusCode, null, ex, isWrite: false);
            }
            throw FromProviderStatus(operation, ex.StatusCode, null, ex, isWrite: false);
        }
        catch (Exception ex) when (IsTransportOrShapeFailure(ex))
        {
            throw ReadFailure(operation, ex);
        }

        if (response.Subscription?.Id is not null)
        {
            return Map(response.Subscription);
        }
        _logger.LogError("Maxio {Operation}: response carried no subscription.", operation);
        throw new BillingException(502, "The billing provider returned a response that could not be processed.");
    }

    /// <summary>
    /// Re-reads Maxio for a subscription we may have created: by reference first, then the customer's subscription
    /// list (same reference, or same plan, live, created no earlier than the claim). Never throws for provider failures.
    /// </summary>
    public async Task<Reconciliation<BillingSubscription>> ReconcileSubscriptionAsync(
        int customerId, string reference, string planHandle, DateTimeOffset claimedAt, CancellationToken cancellationToken)
    {
        try
        {
            var byReference = await FindSubscriptionByReferenceAsync(reference, cancellationToken);
            if (byReference is not null)
            {
                return new Reconciliation<BillingSubscription>(byReference, LookupSucceeded: true);
            }

            var all = await ListCustomerSubscriptionsAsync(customerId, cancellationToken);
            var match = all.FirstOrDefault(s => string.Equals(s.Reference, reference, StringComparison.Ordinal))
                ?? all.Where(s => s.IsLive
                                  && string.Equals(s.PlanHandle, planHandle, StringComparison.Ordinal)
                                  && s.CreatedAt is not null
                                  && s.CreatedAt >= claimedAt - ClockSkewAllowance)
                      .OrderBy(s => s.CreatedAt)
                      .FirstOrDefault();
            return new Reconciliation<BillingSubscription>(match, LookupSucceeded: true);
        }
        catch (BillingException ex)
        {
            _logger.LogWarning("Maxio subscription reconciliation for reference {Reference} could not complete ({Status}).", reference, ex.ProviderStatusCode);
            return new Reconciliation<BillingSubscription>(null, LookupSucceeded: false);
        }
    }

    /// <summary>
    /// Creates a subscription for <paramref name="customerId"/> on <paramref name="planHandle"/>, carrying
    /// <paramref name="reference"/>. Unknown outcomes (connection failure, timeout, 5xx, unreadable 2xx) are settled
    /// here via <see cref="ReconcileSubscriptionAsync"/>; a definite refusal throws <see cref="BillingException"/>.
    /// </summary>
    public async Task<WriteResult<BillingSubscription>> CreateSubscriptionAsync(
        int customerId, string planHandle, string reference, DateTimeOffset claimedAt, CancellationToken cancellationToken)
    {
        const string operation = "CreateSubscription";
        try
        {
            var request = new CreateSubscriptionOperationRequest
            {
                Body = new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = planHandle,
                        CustomerId = customerId,
                        Reference = reference,
                        // The plans need no payment method: collect by remittance (invoice sent to the customer)
                        // rather than the site default, which charges a payment method on file at signup and
                        // was refused live with "No payment method was on file".
                        PaymentCollectionMethod = CollectionMethod.Remittance,
                    }
                }
            };
            var response = await Bounded(t => _client.Subscriptions.CreateSubscription(request, cancellationToken: t), cancellationToken);

            if (response.Subscription?.Id is not null)
            {
                var created = Map(response.Subscription);
                _logger.LogInformation("Maxio subscription {SubscriptionId} created on plan {Plan} for customer {CustomerId} (reference {Reference}).",
                    created.Id, planHandle, customerId, reference);
                return WriteResult<BillingSubscription>.Landed(created);
            }
            _logger.LogError("Maxio {Operation}: 2xx response carried no subscription; settling by reference.", operation);
            return await SettleSubscriptionAsync(customerId, reference, planHandle, claimedAt, cancellationToken);
        }
        catch (ApiException<CreateSubscriptionError> ex) when ((int)ex.StatusCode >= 500)
        {
            _logger.LogError("Maxio {Operation} answered {Status}; outcome unknown, settling by reference.", operation, (int)ex.StatusCode);
            return await SettleSubscriptionAsync(customerId, reference, planHandle, claimedAt, cancellationToken);
        }
        catch (ApiException<CreateSubscriptionError> ex)
        {
            string? detail = null;
            if (ex.Error.TryGetErrorListResponse1(out ErrorListResponse1 body))
            {
                detail = string.Join("; ", body.Errors ?? Array.Empty<string>());
            }
            else if (ex.Error.TryGetRawError(out RawError _))
            {
                detail = null;
            }
            throw FromProviderStatus(operation, ex.StatusCode, detail, ex, isWrite: true);
        }
        catch (ResponseDeserializationException ex) when (IsSuccessOrServerError(ex.StatusCode))
        {
            _logger.LogError("Maxio {Operation}: unreadable {Status} response; settling by reference.", operation, (int)ex.StatusCode);
            return await SettleSubscriptionAsync(customerId, reference, planHandle, claimedAt, cancellationToken);
        }
        catch (ResponseDeserializationException ex)
        {
            throw FromProviderStatus(operation, ex.StatusCode, null, ex, isWrite: true);
        }
        catch (SdkConnectionException ex) // includes SdkTimeoutException: the write may still have landed
        {
            _logger.LogError("Maxio {Operation}: {Failure}; outcome unknown, settling by reference.", operation, ex.GetType().Name);
            return await SettleSubscriptionAsync(customerId, reference, planHandle, claimedAt, cancellationToken);
        }
        catch (CallBudgetExceededException)
        {
            _logger.LogError("Maxio {Operation}: call budget {Budget} exceeded; outcome unknown, settling by reference.", operation, _policy.CallBudget);
            return await SettleSubscriptionAsync(customerId, reference, planHandle, claimedAt, cancellationToken);
        }
        catch (AuthSchemeException ex)
        {
            throw ReadFailure(operation, ex);
        }
    }

    private async Task<WriteResult<BillingSubscription>> SettleSubscriptionAsync(
        int customerId, string reference, string planHandle, DateTimeOffset claimedAt, CancellationToken cancellationToken)
    {
        var reconciliation = await ReconcileSubscriptionAsync(customerId, reference, planHandle, claimedAt, cancellationToken);
        if (reconciliation.Found is not null)
        {
            _logger.LogInformation("Maxio subscription {SubscriptionId} found for reference {Reference} after an unknown outcome.", reconciliation.Found.Id, reference);
            return WriteResult<BillingSubscription>.Landed(reconciliation.Found);
        }
        _logger.LogWarning("Maxio subscription for reference {Reference}: outcome still unknown (lookup succeeded: {LookupSucceeded}).", reference, reconciliation.LookupSucceeded);
        return WriteResult<BillingSubscription>.Unknown();
    }

    // ---------------------------------------------------------------- mapping & failure translation

    private static readonly TimeSpan ClockSkewAllowance = TimeSpan.FromMinutes(2);

    /// <summary>States after which a subscription no longer counts as the user's current subscription.</summary>
    private static readonly SubscriptionState[] TerminalStates =
    {
        SubscriptionState.Canceled,
        SubscriptionState.Expired,
        SubscriptionState.FailedToCreate,
        SubscriptionState.Suspended,
        SubscriptionState.TrialEnded,
    };

    internal static BillingSubscription Map(Subscription subscription)
    {
        var product = subscription.Product;
        var state = subscription.State;
        // Unknown (undeclared) states count as live: conservative, it blocks a duplicate subscribe.
        var isLive = state is null || !TerminalStates.Contains(state);
        return new BillingSubscription(
            subscription.Id ?? 0,
            subscription.Reference,
            state?.Value ?? "unknown",
            isLive,
            product?.Handle,
            product?.Name,
            product?.ProductFamily?.Handle,
            subscription.ProductPriceInCents ?? product?.PriceInCents,
            subscription.Currency,
            product?.Interval,
            product?.IntervalUnit?.Value,
            subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
            subscription.CurrentPeriodEndsAt,
            subscription.CreatedAt,
            subscription.ActivatedAt);
    }

    private static string? Describe(Errors1? errors)
    {
        if (errors is null)
        {
            return null;
        }
        if (errors.TryGetListOfString(out IReadOnlyList<string> list))
        {
            return string.Join("; ", list);
        }
        if (errors.TryGetCustomerError(out CustomerError customerError))
        {
            return customerError.Customer;
        }
        return null;
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(_policy.CallBudget);
        try
        {
            return await call(budget.Token);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Our own budget fired (not the caller): a timeout, not a cancellation.
            throw new CallBudgetExceededException(ex);
        }
    }

    private static bool IsTransportOrShapeFailure(Exception ex) =>
        ex is SdkConnectionException or ResponseDeserializationException or AuthSchemeException or CallBudgetExceededException;

    private static bool IsSuccessOrServerError(HttpStatusCode status) =>
        (int)status is >= 200 and < 300 or >= 500;

    private BillingException ReadFailure(string operation, Exception ex)
    {
        switch (ex)
        {
            case SdkTimeoutException timeout:
                _logger.LogError("Maxio {Operation}: no response within {Timeout}.", operation, timeout.Timeout);
                return new BillingException(504, "The billing provider did not respond in time.", ex);
            case SdkConnectionException:
                _logger.LogError("Maxio {Operation}: connection failure ({Failure}).", operation, ex.InnerException?.GetType().Name ?? ex.GetType().Name);
                return new BillingException(503, "The billing provider could not be reached.", ex);
            case CallBudgetExceededException:
                _logger.LogError("Maxio {Operation}: call budget {Budget} exceeded.", operation, _policy.CallBudget);
                return new BillingException(504, "The billing provider did not respond in time.", ex);
            case ResponseDeserializationException unreadable:
                _logger.LogError("Maxio {Operation}: {Status} response could not be read as {Type}.", operation, (int)unreadable.StatusCode, unreadable.TargetType.Name);
                return new BillingException(502, "The billing provider returned a response that could not be processed.", ex) { ProviderStatusCode = (int)unreadable.StatusCode };
            case AuthSchemeException:
                _logger.LogError("Maxio {Operation}: credentials could not be applied.", operation);
                return new BillingException(502, "The billing provider is unavailable.", ex);
            default:
                _logger.LogError(ex, "Maxio {Operation}: unexpected failure.", operation);
                return new BillingException(502, "The billing provider is unavailable.", ex);
        }
    }

    private BillingException FromProviderStatus(string operation, HttpStatusCode status, string? detail, Exception ex, bool isWrite)
    {
        var code = (int)status;
        switch (code)
        {
            case 401 or 403:
                // OUR credentials are wrong: the caller did nothing wrong and cannot fix it.
                _logger.LogError("Maxio {Operation}: credentials rejected ({Status}).", operation, code);
                return new BillingException(502, "The billing provider is unavailable.", ex) { ProviderStatusCode = code };
            case 429:
                _logger.LogWarning("Maxio {Operation}: rate limited ({Status}).", operation, code);
                return new BillingException(503, "The billing provider is busy; try again shortly.", ex) { ProviderStatusCode = code };
            case >= 400 and < 500 when isWrite:
                _logger.LogWarning("Maxio {Operation} rejected the request ({Status}): {Detail}", operation, code, detail);
                return new BillingException(422,
                    string.IsNullOrWhiteSpace(detail) ? "The billing provider rejected the request." : $"The billing provider rejected the request: {detail}",
                    ex) { ProviderStatusCode = code };
            case >= 400 and < 500:
                _logger.LogError("Maxio {Operation} answered {Status}.", operation, code);
                return new BillingException(502, "The billing provider is unavailable.", ex) { ProviderStatusCode = code };
            default:
                _logger.LogError("Maxio {Operation} answered {Status}.", operation, code);
                return new BillingException(502, "The billing provider is unavailable.", ex) { ProviderStatusCode = code };
        }
    }

    /// <summary>Raised when <see cref="MaxioCallPolicy.CallBudget"/> expires before the SDK call completes.</summary>
    private sealed class CallBudgetExceededException : Exception
    {
        public CallBudgetExceededException(Exception inner) : base("Maxio call budget exceeded.", inner) { }
    }
}
