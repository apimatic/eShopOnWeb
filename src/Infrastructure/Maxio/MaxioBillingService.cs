using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
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
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// <see cref="ISubscriptionBillingService"/> backed by Maxio Advanced Billing (the system of record).
/// Every provider failure is translated to a <see cref="BillingException"/> at this boundary; no SDK
/// or provider exception text is allowed to escape to the caller.
/// </summary>
public class MaxioBillingService : ISubscriptionBillingService
{
    // The whole-call budget the caller actually experiences. The SDK's per-attempt Timeout does not
    // bound a call, so a linked CancellationToken deadline is the only real ceiling.
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    private const int PlansPerPage = 100;
    private const int MaxPlanPages = 20;

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly IAppLogger<MaxioBillingService> _logger;

    public MaxioBillingService(MaxioAdvancedBillingClient client, IOptions<MaxioSettings> settings,
        IAppLogger<MaxioBillingService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    // Chargify accepts the product family in the path either by numeric id or as "handle:<handle>".
    private string ProductFamilyId => "handle:" + _settings.ProductFamilyHandle;

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(async token =>
        {
            var plans = new List<SubscriptionPlan>();

            for (int page = 1; page <= MaxPlanPages; page++)
            {
                IReadOnlyList<ProductResponse> pageItems;
                try
                {
                    pageItems = await _client.ProductFamilies.ListProductsForProductFamily(
                        productFamilyId: ProductFamilyId,
                        dateField: null,
                        filter: null,
                        startDate: null,
                        endDate: null,
                        startDatetime: null,
                        endDatetime: null,
                        includeArchived: false,
                        include: null,
                        page: page,
                        perPage: PlansPerPage,
                        ct: token);
                }
                catch (SdkException<ListProductsForProductFamilyError> ex)
                {
                    // 404 here means the configured product family does not exist on the site.
                    if (ex.Error.TryGetString(out _))
                        throw new BillingException(
                            $"The configured product family '{_settings.ProductFamilyHandle}' was not found.",
                            BillingErrorKind.ProviderUnavailable, ex);
                    if (ex.Error.TryGetRawError(out var raw))
                        throw MapStatus((int)raw.StatusCode, SafeRead(raw), ex);
                    throw new BillingException("Could not list subscription plans.", BillingErrorKind.Unknown, ex);
                }

                foreach (var pr in pageItems)
                {
                    var product = pr.Product;
                    if (product is null || product.ArchivedAt is not null || string.IsNullOrEmpty(product.Handle))
                        continue;

                    plans.Add(new SubscriptionPlan
                    {
                        Handle = product.Handle!,
                        Name = product.Name,
                        Description = product.Description,
                        PriceInCents = product.PriceInCents ?? 0,
                        Interval = product.Interval,
                        IntervalUnit = product.IntervalUnit?.Value
                    });
                }

                if (pageItems.Count < PlansPerPage)
                    break;

                if (page == MaxPlanPages)
                    _logger.LogWarning(
                        "Maxio plan listing hit the {MaxPlanPages}-page cap for family {Family}; result may be truncated.",
                        MaxPlanPages, Arg(_settings.ProductFamilyHandle));
            }

            return (IReadOnlyList<SubscriptionPlan>)plans;
        }, cancellationToken);
    }

    public async Task<SubscriptionDetails> SubscribeAsync(BillingCustomer customer, string planHandle,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
            throw new UnknownPlanException(planHandle ?? string.Empty);

        return await ExecuteAsync(async token =>
        {
            // Cross-operation invariant: the requested plan must be one the family actually offers.
            var plans = await GetPlansInternalAsync(token);
            var plan = plans.FirstOrDefault(p =>
                string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
            if (plan is null)
                throw new UnknownPlanException(planHandle);

            // Ensure a single provider customer for this user (idempotent).
            await EnsureCustomerAsync(customer, token);

            // Deterministic subscription reference = the duplicate-prevention key for (user, plan).
            var subscriptionReference = BuildSubscriptionReference(customer.Reference, plan.Handle);

            // Fast idempotent path: already subscribed → return the existing subscription unchanged.
            var existing = await FindSubscriptionOrNullAsync(subscriptionReference, token);
            if (existing is not null)
            {
                _logger.LogInformation(
                    "Reusing existing subscription {SubscriptionId} (reference {Reference}) for plan {Plan}.",
                    Arg(existing.Id), subscriptionReference, plan.Handle);
                return existing;
            }

            var body = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = plan.Handle,
                    CustomerReference = customer.Reference,
                    Reference = subscriptionReference,
                    // The plans are configured "payment method not required", so collect by invoice
                    // (remittance) rather than automatically charging a card at signup — this lets the
                    // subscription activate without card capture / 3-DS.
                    PaymentCollectionMethod = CollectionMethod.Remittance
                }
            };

            try
            {
                var response = await _client.Subscriptions.CreateSubscription(body, ct: token);
                var details = MapSubscription(response.Subscription, fallbackReference: subscriptionReference);

                _logger.LogInformation(
                    "Created subscription {SubscriptionId} (reference {Reference}) for plan {Plan} in state {State}.",
                    Arg(details.Id), subscriptionReference, plan.Handle, Arg(details.State));

                if (!IsHealthyState(details.State))
                    _logger.LogWarning(
                        "Subscription {SubscriptionId} for plan {Plan} was created in non-active state {State}.",
                        Arg(details.Id), plan.Handle, Arg(details.State));

                return details;
            }
            catch (SdkException<CreateSubscriptionError> ex)
            {
                // A provider rejection. If it is the "reference already taken" race, reconcile.
                var reconciled = await FindSubscriptionOrNullAsync(subscriptionReference, token);
                if (reconciled is not null)
                {
                    _logger.LogWarning(
                        "Reconciled concurrent subscription create for reference {Reference}.", subscriptionReference);
                    return reconciled;
                }

                throw new BillingException(
                    "Could not create subscription: " + DescribeSubscriptionError(ex.Error),
                    BillingErrorKind.InvalidRequest, ex);
            }
            catch (Exception ex) when (IsUnknownOutcome(ex, token))
            {
                // Transport/timeout after the request may have been received — outcome unknown.
                // Re-read by reference before declaring anything (never a definite failure blind).
                var reconciled = await FindSubscriptionOrNullAsync(subscriptionReference, token);
                if (reconciled is not null)
                {
                    _logger.LogWarning(
                        "Recovered subscription {SubscriptionId} after transport failure (reference {Reference}).",
                        Arg(reconciled.Id), subscriptionReference);
                    return reconciled;
                }

                throw new BillingException(
                    "The subscription request could not be confirmed with the billing provider.",
                    BillingErrorKind.ProviderUnavailable, ex);
            }
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<SubscriptionDetails>> GetSubscriptionsAsync(BillingCustomer customer,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(async token =>
        {
            var found = await ReadCustomerOrNullAsync(customer.Reference, token);
            if (found?.Id is null)
                return (IReadOnlyList<SubscriptionDetails>)Array.Empty<SubscriptionDetails>();

            var responses = await _client.Customers.ListCustomerSubscriptions(found.Id.Value, ct: token);

            var result = new List<SubscriptionDetails>();
            foreach (var response in responses)
            {
                if (response.Subscription is not null)
                    result.Add(MapSubscription(response.Subscription, fallbackReference: null));
            }

            return (IReadOnlyList<SubscriptionDetails>)result;
        }, cancellationToken);
    }

    // ----- internal helpers -----

    private async Task<IReadOnlyList<SubscriptionPlan>> GetPlansInternalAsync(CancellationToken token)
    {
        // Same listing as GetPlansAsync but without a fresh budget/wrapper (already inside one).
        var plans = new List<SubscriptionPlan>();
        for (int page = 1; page <= MaxPlanPages; page++)
        {
            IReadOnlyList<ProductResponse> pageItems;
            try
            {
                pageItems = await _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: ProductFamilyId,
                    dateField: null, filter: null, startDate: null, endDate: null,
                    startDatetime: null, endDatetime: null,
                    includeArchived: false, include: null,
                    page: page, perPage: PlansPerPage, ct: token);
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetString(out _))
                    throw new BillingException(
                        $"The configured product family '{_settings.ProductFamilyHandle}' was not found.",
                        BillingErrorKind.ProviderUnavailable, ex);
                if (ex.Error.TryGetRawError(out var raw))
                    throw MapStatus((int)raw.StatusCode, SafeRead(raw), ex);
                throw new BillingException("Could not list subscription plans.", BillingErrorKind.Unknown, ex);
            }

            foreach (var pr in pageItems)
            {
                var product = pr.Product;
                if (product is null || product.ArchivedAt is not null || string.IsNullOrEmpty(product.Handle))
                    continue;
                plans.Add(new SubscriptionPlan
                {
                    Handle = product.Handle!,
                    Name = product.Name,
                    Description = product.Description,
                    PriceInCents = product.PriceInCents ?? 0,
                    Interval = product.Interval,
                    IntervalUnit = product.IntervalUnit?.Value
                });
            }

            if (pageItems.Count < PlansPerPage)
                break;
        }

        return plans;
    }

    private async Task EnsureCustomerAsync(BillingCustomer customer, CancellationToken token)
    {
        var existing = await ReadCustomerOrNullAsync(customer.Reference, token);
        if (existing is not null)
            return;

        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = customer.FirstName,
                LastName = customer.LastName,
                Email = customer.Email,
                Reference = customer.Reference
            }
        };

        try
        {
            var response = await _client.Customers.CreateCustomer(body, ct: token);
            _logger.LogInformation(
                "Created billing customer {CustomerId} for reference {Reference}.",
                Arg(response.Customer.Id), customer.Reference);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            var reconciled = await ReadCustomerOrNullAsync(customer.Reference, token);
            if (reconciled is not null)
            {
                _logger.LogWarning(
                    "Reconciled concurrent customer create for reference {Reference}.", customer.Reference);
                return;
            }

            throw new BillingException(
                "Could not create billing customer: " + DescribeCustomerError(ex.Error),
                BillingErrorKind.InvalidRequest, ex);
        }
        catch (Exception ex) when (IsUnknownOutcome(ex, token))
        {
            var reconciled = await ReadCustomerOrNullAsync(customer.Reference, token);
            if (reconciled is not null)
            {
                _logger.LogWarning(
                    "Recovered billing customer after transport failure (reference {Reference}).",
                    customer.Reference);
                return;
            }

            throw new BillingException(
                "The billing customer could not be confirmed with the provider.",
                BillingErrorKind.ProviderUnavailable, ex);
        }
    }

    private async Task<Customer?> ReadCustomerOrNullAsync(string reference, CancellationToken token)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference, ct: token);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<SubscriptionDetails?> FindSubscriptionOrNullAsync(string reference, CancellationToken token)
    {
        try
        {
            var response = await _client.Subscriptions.FindSubscription(reference, ct: token);
            return response.Subscription is null ? null : MapSubscription(response.Subscription, reference);
        }
        catch (SdkException<FindSubscriptionError> ex) when (ex.Error.TryGetNoContent(out _))
        {
            return null;
        }
    }

    /// <summary>
    /// Wraps a unit of provider work with a whole-call deadline and the shared failure-translation
    /// boundary. Call-site catches (typed errors, lookup 404s, unknown-outcome reconciliation) run
    /// first; this catches everything they intentionally leave to the boundary.
    /// </summary>
    private async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);

        try
        {
            return await action(cts.Token);
        }
        catch (BillingException)
        {
            throw; // already translated at a call site
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // caller/host cancellation — propagate as-is
        }
        catch (OperationCanceledException ex)
        {
            throw new BillingException(
                "The billing provider did not respond within the allotted time.",
                BillingErrorKind.ProviderUnavailable, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new BillingException(
                "The billing provider is unreachable.", BillingErrorKind.ProviderUnavailable, ex);
        }
        catch (AuthSchemeException ex)
        {
            throw new BillingException(
                "The billing provider rejected our credentials.", BillingErrorKind.ProviderUnavailable, ex);
        }
        catch (SdkException<RawError> ex)
        {
            throw MapStatus((int)ex.Error.StatusCode, SafeRead(ex.Error), ex);
        }
        catch (JsonException ex)
        {
            // A 2xx body that no longer matches the model — outcome unknown, never blamed on the caller.
            throw new BillingException(
                "The billing provider returned a response that could not be processed.",
                BillingErrorKind.Unknown, ex);
        }
    }

    private static bool IsUnknownOutcome(Exception ex, CancellationToken token) =>
        !token.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException;

    private BillingException MapStatus(int status, string detail, Exception inner)
    {
        // Our credentials / our quota — the caller did nothing wrong and cannot fix it.
        if (status is 401 or 403 or 429)
            return new BillingException("The billing provider is currently unavailable.",
                BillingErrorKind.ProviderUnavailable, inner);
        if (status == 404)
            return new BillingException("The requested billing resource was not found.",
                BillingErrorKind.NotFound, inner);
        if (status is >= 400 and < 500)
        {
            _logger.LogWarning("Maxio returned {Status}: {Detail}", status, detail);
            return new BillingException("The billing provider rejected the request.",
                BillingErrorKind.InvalidRequest, inner);
        }
        return new BillingException("The billing provider is currently unavailable.",
            BillingErrorKind.ProviderUnavailable, inner);
    }

    private static string DescribeSubscriptionError(CreateSubscriptionError error)
    {
        if (error.TryGetErrorListResponse1(out var body) && body.Errors is { Count: > 0 })
            return string.Join("; ", body.Errors);
        if (error.TryGetRawError(out var raw))
            return SafeRead(raw);
        return "unrecognised provider error.";
    }

    private static string DescribeCustomerError(CreateCustomerError error)
    {
        if (error.TryGetCustomerErrorResponse1(out var body) && body.Errors is not null)
        {
            if (body.Errors.TryGetListOfString(out var messages) && messages.Count > 0)
                return string.Join("; ", messages);
            return "validation failed.";
        }
        if (error.TryGetRawError(out var raw))
            return SafeRead(raw);
        return "unrecognised provider error.";
    }

    private static string SafeRead(RawError raw)
    {
        try
        {
            var text = raw.ReadAsString();
            return string.IsNullOrWhiteSpace(text) ? $"HTTP {(int)raw.StatusCode}" : text;
        }
        catch
        {
            return $"HTTP {(int)raw.StatusCode}";
        }
    }

    private static bool IsHealthyState(string? state) =>
        state is "active" or "trialing";

    private static SubscriptionDetails MapSubscription(Subscription? subscription, string? fallbackReference)
    {
        if (subscription is null)
            return new SubscriptionDetails { Reference = fallbackReference };

        return new SubscriptionDetails
        {
            Id = subscription.Id,
            Reference = subscription.Reference ?? fallbackReference,
            PlanHandle = subscription.Product?.Handle,
            PlanName = subscription.Product?.Name,
            PriceInCents = subscription.ProductPriceInCents,
            Currency = subscription.Currency,
            State = subscription.State?.Value,
            NextBillingDate = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt,
            CreatedAt = subscription.CreatedAt
        };
    }

    private static string BuildSubscriptionReference(string customerReference, string planHandle) =>
        Sanitize($"eshop-sub-{customerReference}-{planHandle}");

    // Coerces a possibly-null value to a non-null object for structured-logging args.
    private static object Arg(object? value) => value ?? "unknown";

    internal static string Sanitize(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c) || c is '-' or '_' or '.')
                sb.Append(c);
            else
                sb.Append('-');
        }
        return sb.ToString();
    }
}
