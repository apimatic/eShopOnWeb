using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// <see cref="ISubscriptionBillingService"/> implemented against the Maxio Advanced Billing SDK.
/// Maxio is the system of record: idempotency is enforced against Maxio state (customer keyed by
/// <c>reference</c>, no second live subscription to the same plan) rather than local persistence,
/// which the in-memory database would not survive.
/// </summary>
public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    // Whole-call budget: one deadline for every SDK call an operation makes. The SDK's own
    // Timeout is per-attempt, so a CancellationToken deadline is the only thing that bounds a
    // whole operation (which may chain several calls).
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    // Subscription states that are NOT live (terminal / dead). Anything else — including unknown
    // future states — is treated as live, so the idempotency guard errs toward not duplicating.
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "unpaid", "suspended", "trial_ended", "failed_to_create"
    };

    private const int PageSize = 100;
    private const int MaxPlanPages = 20;

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);

        var products = await ListPlanProductsAsync(cts.Token, cancellationToken);
        return MapLivePlans(products);
    }

    public async Task<CustomerSubscription> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        var deadline = cts.Token;

        // 1. Resolve the target plan against the configured family (cross-operation invariant: a
        //    plan handle we subscribe to must be one the family actually offers).
        var plans = MapLivePlans(await ListPlanProductsAsync(deadline, cancellationToken));
        if (plans.Count == 0)
        {
            throw new BillingException(
                "No subscription plans are currently available.",
                BillingErrorKind.ProviderUnavailable);
        }

        SubscriptionPlan plan;
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            plan = plans[0]; // default target: first available plan
        }
        else
        {
            var match = plans.FirstOrDefault(p =>
                string.Equals(p.Handle, request.PlanHandle, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                throw new BillingException(
                    $"Plan '{request.PlanHandle}' is not available.",
                    BillingErrorKind.Validation);
            }

            plan = match;
        }

        // 2. Ensure a Maxio customer exists for this shopper (idempotent).
        var customerId = await EnsureCustomerAsync(request, deadline, cancellationToken);

        // 3. Idempotency guard: if a live subscription to this plan already exists, return it
        //    rather than creating a second one (double-click safety, enforced against Maxio).
        var existing = await ListCustomerSubscriptionsAsync(customerId, deadline, cancellationToken);
        var live = existing.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase)
            && IsLive(s.State));
        if (live is not null)
        {
            _logger.LogInformation(
                "Existing live subscription {SubscriptionId} found for customer {CustomerId} plan {PlanHandle}; not creating a duplicate.",
                live.Id, customerId, plan.Handle);
            return MapSubscription(live);
        }

        // 4. Enroll.
        var created = await CreateSubscriptionAsync(customerId, plan.Handle, request.UserReference, deadline, cancellationToken);
        _logger.LogInformation(
            "Created subscription {SubscriptionId} ({State}) for customer {CustomerId} plan {PlanHandle}.",
            created.Id, created.State, customerId, plan.Handle);
        return created;
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(string userReference, CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        var deadline = cts.Token;

        var customerId = await FindCustomerIdAsync(userReference, deadline, cancellationToken);
        if (customerId is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await ListCustomerSubscriptionsAsync(customerId.Value, deadline, cancellationToken);
        return subscriptions.Select(MapSubscription).ToList();
    }

    // --- Maxio calls (each translates its own error case to BillingException) ---

    private async Task<IReadOnlyList<ProductResponse>> ListPlanProductsAsync(CancellationToken deadline, CancellationToken callerCt)
    {
        var familyHandle = _settings.ProductFamilyHandle!;
        // The endpoint's product_family_id accepts either a numeric id or a handle prefixed with
        // "handle:" (per the SDK source's <param> doc). The configured value is a handle.
        var familyId = ToProductFamilyIdentifier(familyHandle);
        var all = new List<ProductResponse>();

        try
        {
            for (var page = 1; page <= MaxPlanPages; page++)
            {
                var batch = await _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: familyId,
                    dateField: null,
                    filter: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    includeArchived: false,
                    include: null,
                    page: page,
                    perPage: PageSize,
                    ct: deadline);

                all.AddRange(batch);
                if (batch.Count < PageSize)
                {
                    return all;
                }
            }

            _logger.LogWarning(
                "Plan listing for family {FamilyHandle} hit the {MaxPlanPages}-page cap; results may be truncated.",
                familyHandle, MaxPlanPages);
            return all;
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out var notFound))
            {
                // 404 on the configured family handle is a misconfiguration, not a caller error.
                _logger.LogError("Configured Maxio product family {FamilyHandle} was not found: {Detail}", familyHandle, notFound);
                throw new BillingException(
                    "The configured billing product family was not found.",
                    BillingErrorKind.ProviderUnavailable, 404, innerException: ex);
            }

            throw Translate("list plans", ex);
        }
        catch (OperationCanceledException) when (callerCt.IsCancellationRequested) { throw; }
        catch (Exception ex) { throw Translate("list plans", ex); }
    }

    private async Task<int?> FindCustomerIdAsync(string reference, CancellationToken deadline, CancellationToken callerCt)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference: reference, ct: deadline);
            return response.Customer.Id;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null; // no customer for this reference yet
        }
        catch (OperationCanceledException) when (callerCt.IsCancellationRequested) { throw; }
        catch (Exception ex) { throw Translate("look up customer", ex); }
    }

    private async Task<int> EnsureCustomerAsync(SubscribeRequest request, CancellationToken deadline, CancellationToken callerCt)
    {
        var existing = await FindCustomerIdAsync(request.UserReference, deadline, callerCt);
        if (existing is int found)
        {
            return found;
        }

        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = request.FirstName,
                LastName = request.LastName,
                Email = request.Email,
                Reference = request.UserReference // idempotency / durable user link
            }
        };

        try
        {
            var response = await _client.Customers.CreateCustomer(body, ct: deadline);
            var id = response.Customer.Id
                ?? throw new BillingException("The billing provider did not return a customer id.", BillingErrorKind.Unexpected);
            _logger.LogInformation("Created Maxio customer {CustomerId} for reference {Reference}.", id, request.UserReference);
            return id;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // A concurrent (double-submitted) request may have created the customer between our
            // read and create — reconcile against Maxio before treating this as a failure.
            var raced = await FindCustomerIdAsync(request.UserReference, deadline, callerCt);
            if (raced is int racedId)
            {
                _logger.LogInformation("Reconciled concurrent customer creation for reference {Reference}.", request.UserReference);
                return racedId;
            }

            var messages = ExtractCustomerErrors(ex.Error);
            _logger.LogWarning("Maxio rejected customer creation: {Messages}", string.Join("; ", messages));
            throw new BillingException(
                "The billing provider rejected the customer details.",
                BillingErrorKind.Validation, 422, messages, ex);
        }
        catch (OperationCanceledException) when (callerCt.IsCancellationRequested) { throw; }
        catch (Exception ex) { throw Translate("create customer", ex); }
    }

    private async Task<IReadOnlyList<Subscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken deadline, CancellationToken callerCt)
    {
        try
        {
            var response = await _client.Customers.ListCustomerSubscriptions(customerId, ct: deadline);
            return response
                .Select(r => r.Subscription)
                .Where(s => s is not null)
                .Cast<Subscription>()
                .ToList();
        }
        catch (OperationCanceledException) when (callerCt.IsCancellationRequested) { throw; }
        catch (Exception ex) { throw Translate("list customer subscriptions", ex); }
    }

    private async Task<CustomerSubscription> CreateSubscriptionAsync(
        int customerId, string planHandle, string userReference, CancellationToken deadline, CancellationToken callerCt)
    {
        var body = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = planHandle,
                CustomerId = customerId,
                Reference = $"eshop-{userReference}-{planHandle}" // deterministic, for traceability
            }
        };

        try
        {
            var response = await _client.Subscriptions.CreateSubscription(body, ct: deadline);
            var subscription = response.Subscription
                ?? throw new BillingException("The billing provider did not return a subscription.", BillingErrorKind.Unexpected);
            return MapSubscription(subscription);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            var messages = ExtractSubscriptionErrors(ex.Error);
            _logger.LogWarning("Maxio rejected subscription creation: {Messages}", string.Join("; ", messages));
            throw new BillingException(
                "The billing provider rejected the subscription request.",
                BillingErrorKind.Validation, 422, messages, ex);
        }
        catch (OperationCanceledException) when (callerCt.IsCancellationRequested) { throw; }
        catch (Exception ex) { throw Translate("create subscription", ex); }
    }

    // --- Error translation, mapping, and helpers ---

    private BillingException Translate(string operation, Exception ex)
    {
        switch (ex)
        {
            case SdkException<RawError> raw:
            {
                var status = (int)raw.Error.StatusCode;
                var kind = status switch
                {
                    401 or 403 or 429 => BillingErrorKind.ProviderUnavailable, // our credentials / quota
                    404 => BillingErrorKind.NotFound,
                    >= 400 and < 500 => BillingErrorKind.Validation,
                    _ => BillingErrorKind.ProviderUnavailable
                };
                _logger.LogWarning("Maxio {Operation} failed: HTTP {Status}.", operation, status);
                return new BillingException(
                    $"The billing provider could not complete '{operation}'.", kind, status, innerException: ex);
            }

            case JsonException:
                // A drifted 2xx body, or an error body that did not match its generated error
                // shape, surfaces here — outcome cannot be trusted.
                _logger.LogError(ex, "Maxio {Operation} returned a response that could not be processed.", operation);
                return new BillingException(
                    $"The billing provider returned a response for '{operation}' that could not be processed.",
                    BillingErrorKind.Unexpected, innerException: ex);

            case HttpRequestException:
            case TaskCanceledException:
            case TimeoutException:
                _logger.LogError(ex, "Maxio {Operation} could not reach the provider.", operation);
                return new BillingException(
                    $"The billing provider is currently unavailable ('{operation}').",
                    BillingErrorKind.ProviderUnavailable, innerException: ex);

            case BillingException billing:
                return billing; // already translated

            default:
                _logger.LogError(ex, "Maxio {Operation} failed unexpectedly.", operation);
                return new BillingException(
                    $"An unexpected error occurred calling the billing provider ('{operation}').",
                    BillingErrorKind.Unexpected, innerException: ex);
        }
    }

    private static IReadOnlyList<string> ExtractCustomerErrors(CreateCustomerError error)
    {
        if (error.TryGetCustomerErrorResponse1(out var body) && body.Errors is { } errors)
        {
            if (errors.TryGetListOfString(out var list))
            {
                return list.ToList();
            }

            if (errors.TryGetCustomerError(out var customerError) && !string.IsNullOrWhiteSpace(customerError.Customer))
            {
                return new[] { customerError.Customer! };
            }
        }

        if (error.TryGetRawError(out var raw))
        {
            return new[] { raw.ReadAsString() };
        }

        return Array.Empty<string>();
    }

    private static IReadOnlyList<string> ExtractSubscriptionErrors(CreateSubscriptionError error)
    {
        if (error.TryGetErrorListResponse1(out var body))
        {
            return body.Errors.ToList();
        }

        if (error.TryGetRawError(out var raw))
        {
            return new[] { raw.ReadAsString() };
        }

        return Array.Empty<string>();
    }

    private static IReadOnlyList<SubscriptionPlan> MapLivePlans(IReadOnlyList<ProductResponse> products) =>
        products
            .Select(p => p.Product)
            .Where(p => p is not null && p.ArchivedAt is null && !string.IsNullOrWhiteSpace(p.Handle))
            .Select(MapPlan)
            .ToList();

    private static SubscriptionPlan MapPlan(Product product) => new()
    {
        Handle = product.Handle!,
        Name = product.Name,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit?.Value,
        ProductFamilyHandle = product.ProductFamily?.Handle
    };

    private static CustomerSubscription MapSubscription(Subscription subscription) => new()
    {
        Id = subscription.Id ?? 0,
        PlanHandle = subscription.Product?.Handle,
        PlanName = subscription.Product?.Name,
        State = subscription.State?.Value,
        PriceInCents = subscription.ProductPriceInCents,
        Currency = subscription.Currency,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingDate = subscription.NextAssessmentAt,
        Reference = subscription.Reference
    };

    private static bool IsLive(SubscriptionState? state) =>
        state is not null && !TerminalStates.Contains(state.Value);

    /// <summary>
    /// Formats the configured product-family value for the <c>product_family_id</c> path parameter,
    /// which accepts a numeric id or a handle prefixed with <c>handle:</c>. A configured handle is
    /// prefixed; an already-prefixed or purely numeric value is used as-is.
    /// </summary>
    private static string ToProductFamilyIdentifier(string configured) =>
        configured.StartsWith("handle:", StringComparison.OrdinalIgnoreCase) || configured.All(char.IsDigit)
            ? configured
            : $"handle:{configured}";
}
