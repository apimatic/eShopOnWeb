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
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Requests.Customers;
using MaxioAdvancedBilling.Requests.ProductFamilies;
using MaxioAdvancedBilling.Requests.Subscriptions;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// The single boundary between eShop and the Maxio Advanced Billing SDK. Every SDK failure is
/// translated here into <see cref="BillingProviderException"/> (caller-safe message, provider status
/// kept); a write whose outcome cannot be settled becomes <see cref="BillingOutcomeUnknownException"/>.
/// </summary>
public class MaxioBillingGateway : ISubscriptionBillingGateway
{
    /// <summary>Provider maximum page size for the product list.</summary>
    public const int PlanPageSize = 200;
    /// <summary>Provider-independent stop for the plan page walk.</summary>
    public const int MaxPlanPages = 5;

    private static readonly HashSet<string> LiveStates = new(StringComparer.Ordinal)
    {
        SubscriptionState.Active.Value, SubscriptionState.Trialing.Value, SubscriptionState.Assessing.Value,
        SubscriptionState.Pending.Value, SubscriptionState.PastDue.Value, SubscriptionState.SoftFailure.Value,
        SubscriptionState.Unpaid.Value, SubscriptionState.AwaitingSignup.Value, SubscriptionState.Paused.Value
    };

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioBillingGateway> _logger;
    private readonly CollectionMethod? _collectionMethod;

    public MaxioBillingGateway(MaxioAdvancedBillingClient client, IOptions<MaxioSettings> settings, ILogger<MaxioBillingGateway> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
        // Validated at startup (MaxioSettingsValidator).
        CollectionMethod.TryGetKnownValue(_settings.PaymentCollectionMethod, out _collectionMethod);
    }

    public async Task<BillingPlanCatalog> GetPlansAsync(CancellationToken cancellationToken)
    {
        var familyHandle = _settings.ProductFamilyHandle!;
        var plans = new List<BillingPlan>();
        var truncated = false;

        for (var page = 1; ; page++)
        {
            IReadOnlyList<ProductResponse> products;
            try
            {
                products = await Bounded("ListProductsForProductFamily", ct => _client.ProductFamilies.ListProductsForProductFamily(
                    new ListProductsForProductFamilyRequest
                    {
                        ProductFamilyId = "handle:" + familyHandle,
                        Page = page,
                        PerPage = PlanPageSize,
                        IncludeArchived = false
                    },
                    cancellationToken: ct), cancellationToken);
            }
            catch (ApiException<ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetString(out var notFound))
                {
                    _logger.LogError("Maxio product family {ProductFamilyHandle} was not found: {ProviderMessage}", familyHandle, notFound);
                    throw new BillingProviderException(BillingFailureKind.Misconfigured,
                        "The subscription catalog is not available.", (int)ex.StatusCode, innerException: ex);
                }
                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw FromStatus("ListProductsForProductFamily", ex.StatusCode, raw.ReadAsString(), ex);
                }
                throw FromStatus("ListProductsForProductFamily", ex.StatusCode, null, ex);
            }

            plans.AddRange(products
                .Select(p => p.Product)
                .Where(p => p is not null && p.Id is not null && !string.IsNullOrEmpty(p.Handle) && p.ArchivedAt is null)
                .Select(ToPlan));

            if (products.Count < PlanPageSize)
            {
                break;
            }
            if (page >= MaxPlanPages)
            {
                truncated = true;
                _logger.LogWarning("Maxio plan catalog for {ProductFamilyHandle} exceeds {MaxPlans} products; result truncated.",
                    familyHandle, PlanPageSize * MaxPlanPages);
                break;
            }
        }

        return new BillingPlanCatalog(familyHandle, plans, truncated);
    }

    public async Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Bounded("ReadCustomerByReference", ct => _client.Customers.ReadCustomerByReference(
                new ReadCustomerByReferenceRequest { Reference = reference },
                cancellationToken: ct), cancellationToken, translateRawErrors: false);
            return ToCustomer(response.Customer);
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null; // the provider has no customer with this reference
        }
        catch (ApiException<RawError> ex)
        {
            throw FromStatus("ReadCustomerByReference", ex.StatusCode, ex.Error.ReadAsString(), ex);
        }
    }

    public async Task<BillingCustomer> CreateCustomerAsync(NewBillingCustomer customer, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Bounded("CreateCustomer", ct => _client.Customers.CreateCustomer(
                new CreateCustomerOperationRequest
                {
                    Body = new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer
                        {
                            FirstName = customer.FirstName,
                            LastName = customer.LastName,
                            Email = customer.Email,
                            Reference = customer.Reference
                        }
                    }
                },
                cancellationToken: ct), cancellationToken);
            return ToCustomer(response.Customer);
        }
        catch (ApiException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var body))
            {
                throw Rejected("CreateCustomer", ex.StatusCode, ReadErrors(body.Errors), ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromStatus("CreateCustomer", ex.StatusCode, raw.ReadAsString(), ex);
            }
            throw FromStatus("CreateCustomer", ex.StatusCode, null, ex);
        }
        catch (MaxioAmbiguousOutcomeException ex)
        {
            // The customer may have been created. The reference is unique per customer: settle by re-reading it.
            _logger.LogWarning(ex, "CreateCustomer outcome unknown; re-reading customer by reference.");
            BillingCustomer? existing;
            try
            {
                existing = await FindCustomerByReferenceAsync(customer.Reference, cancellationToken);
            }
            catch (BillingProviderException readFailure)
            {
                throw new BillingOutcomeUnknownException("The billing customer could not be confirmed.", readFailure);
            }
            return existing ?? throw new BillingProviderException(BillingFailureKind.Unavailable,
                "The billing provider did not complete the request. Please try again.", innerException: ex);
        }
    }

    public async Task<IReadOnlyList<BillingSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        var responses = await Bounded("ListCustomerSubscriptions", ct => _client.Customers.ListCustomerSubscriptions(
            new ListCustomerSubscriptionsRequest { CustomerId = customerId },
            cancellationToken: ct), cancellationToken);

        return responses
            .Select(r => r.Subscription)
            .Where(s => s?.Id is not null)
            .Select(s => ToSubscription(s!))
            .ToList();
    }

    public async Task<BillingSubscription> CreateSubscriptionAsync(int customerId, string planHandle, string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Bounded("CreateSubscription", ct => _client.Subscriptions.CreateSubscription(
                new CreateSubscriptionOperationRequest
                {
                    Body = new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            ProductHandle = planHandle,
                            CustomerId = customerId,
                            Reference = reference,
                            PaymentCollectionMethod = _collectionMethod
                        }
                    }
                },
                cancellationToken: ct), cancellationToken);

            if (response.Subscription?.Id is null)
            {
                // A 2xx without a subscription: the write probably landed but we cannot read it.
                return await SettleSubscriptionAsync(reference, null, cancellationToken);
            }
            return ToSubscription(response.Subscription);
        }
        catch (ApiException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var body))
            {
                throw Rejected("CreateSubscription", ex.StatusCode, body.Errors ?? Array.Empty<string>(), ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromStatus("CreateSubscription", ex.StatusCode, raw.ReadAsString(), ex);
            }
            throw FromStatus("CreateSubscription", ex.StatusCode, null, ex);
        }
        catch (MaxioAmbiguousOutcomeException ex)
        {
            // The subscription may have been created: settle by looking it up by the reference we sent.
            return await SettleSubscriptionAsync(reference, ex, cancellationToken);
        }
    }

    public async Task<BillingSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Bounded("FindSubscription", ct => _client.Subscriptions.FindSubscription(
                new FindSubscriptionRequest { Reference = reference },
                cancellationToken: ct), cancellationToken);
            return response.Subscription?.Id is null ? null : ToSubscription(response.Subscription);
        }
        catch (ApiException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                return null; // 404: no subscription with this reference
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromStatus("FindSubscription", ex.StatusCode, raw.ReadAsString(), ex);
            }
            throw FromStatus("FindSubscription", ex.StatusCode, null, ex);
        }
    }

    private async Task<BillingSubscription> SettleSubscriptionAsync(string reference, Exception? cause, CancellationToken cancellationToken)
    {
        _logger.LogWarning(cause, "CreateSubscription outcome unknown for reference {SubscriptionReference}; re-reading by reference.", reference);
        BillingSubscription? found;
        try
        {
            found = await FindSubscriptionByReferenceAsync(reference, cancellationToken);
        }
        catch (BillingProviderException readFailure)
        {
            _logger.LogError(readFailure, "Subscription {SubscriptionReference} could not be confirmed; recorded as unknown.", reference);
            throw new BillingOutcomeUnknownException("The subscription could not be confirmed yet.", readFailure);
        }
        if (found is not null)
        {
            return found;
        }
        // The lookup answered and found nothing: the write did not land.
        throw new BillingProviderException(BillingFailureKind.Unavailable,
            "The billing provider did not complete the request. Please try again.", innerException: cause);
    }

    /// <summary>
    /// Runs one SDK call under the whole-call budget (linked to the caller's token) and translates
    /// every non-operation-specific failure. Typed <c>ApiException&lt;{Operation}Error&gt;</c> passes
    /// through to the operation's own catch.
    /// </summary>
    private async Task<T> Bounded<T>(string operation, Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken,
        bool translateRawErrors = true)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(_settings.CallBudgetSeconds));
        try
        {
            return await call(budget.Token);
        }
        catch (ApiException<RawError> ex) when (translateRawErrors)
        {
            throw FromStatus(operation, ex.StatusCode, ex.Error.ReadAsString(), ex);
        }
        catch (ResponseDeserializationException ex)
        {
            if ((int)ex.StatusCode is >= 200 and < 300)
            {
                // The provider answered success but we cannot read it: for a write the outcome is unknown.
                _logger.LogError(ex, "Maxio {Operation} returned an unreadable success body ({TargetType}).", operation, ex.TargetType.Name);
                throw new MaxioAmbiguousOutcomeException(operation, ex);
            }
            _logger.LogWarning(ex, "Maxio {Operation} returned an unreadable error body (HTTP {Status}).", operation, (int)ex.StatusCode);
            throw FromStatus(operation, ex.StatusCode, null, ex);
        }
        catch (SdkConnectionException ex) // includes SdkTimeoutException
        {
            _logger.LogWarning(ex, "Maxio {Operation} got no usable response.", operation);
            throw new MaxioAmbiguousOutcomeException(operation, ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested && budget.IsCancellationRequested)
        {
            _logger.LogWarning("Maxio {Operation} exceeded its {Budget}s budget.", operation, _settings.CallBudgetSeconds);
            throw new MaxioAmbiguousOutcomeException(operation, ex);
        }
        catch (AuthSchemeException ex)
        {
            _logger.LogError(ex, "Maxio credentials could not be applied for {Operation}.", operation);
            throw new BillingProviderException(BillingFailureKind.Misconfigured, "The billing provider is unavailable.", innerException: ex);
        }
    }

    private BillingProviderException Rejected(string operation, HttpStatusCode status, IReadOnlyList<string> errors, Exception inner)
    {
        _logger.LogWarning("Maxio {Operation} rejected the request (HTTP {Status}): {ProviderErrors}", operation, (int)status, string.Join("; ", errors));
        return new BillingProviderException(BillingFailureKind.Rejected, "The billing provider rejected the request.", (int)status, errors, inner);
    }

    private BillingProviderException FromStatus(string operation, HttpStatusCode status, string? body, Exception inner)
    {
        var code = (int)status;
        _logger.LogWarning(inner, "Maxio {Operation} failed with HTTP {Status}.", operation, code);
        return code switch
        {
            401 or 403 => new BillingProviderException(BillingFailureKind.Misconfigured, "The billing provider is unavailable.", code, innerException: inner),
            429 => new BillingProviderException(BillingFailureKind.Throttled, "The billing provider is busy. Please try again shortly.", code, innerException: inner),
            404 => new BillingProviderException(BillingFailureKind.Misconfigured, "The billing provider is unavailable.", code, innerException: inner),
            >= 400 and < 500 => new BillingProviderException(BillingFailureKind.Rejected, "The billing provider rejected the request.", code,
                ExtractErrors(body), inner),
            _ => new BillingProviderException(BillingFailureKind.Unavailable, "The billing provider is unavailable.", code, innerException: inner)
        };
    }

    private static IReadOnlyList<string> ExtractErrors(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return Array.Empty<string>();
        }
        try
        {
            var parsed = System.Text.Json.JsonSerializer.Deserialize<ErrorListResponse1>(body);
            return parsed?.Errors ?? (IReadOnlyList<string>)Array.Empty<string>();
        }
        catch (System.Text.Json.JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static IReadOnlyList<string> ReadErrors(MaxioAdvancedBilling.Models.AnyOf.Errors1? errors)
    {
        if (errors is null)
        {
            return Array.Empty<string>();
        }
        if (errors.TryGetListOfString(out var list))
        {
            return list;
        }
        if (errors.TryGetCustomerError(out var customerError) && !string.IsNullOrEmpty(customerError.Customer))
        {
            return new[] { customerError.Customer };
        }
        return Array.Empty<string>();
    }

    private static BillingCustomer ToCustomer(Customer customer)
    {
        if (customer?.Id is not int id)
        {
            throw new BillingProviderException(BillingFailureKind.UnreadableResponse, "The billing provider returned an incomplete customer.");
        }
        return new BillingCustomer(id, customer.Reference, customer.Email);
    }

    private static BillingPlan ToPlan(Product? product) => new(
        product!.Id!.Value,
        product.Handle!,
        product.Name ?? product.Handle!,
        product.Description,
        product.PriceInCents ?? 0,
        product.Interval,
        product.IntervalUnit?.Value,
        product.RequireCreditCard ?? false);

    private static BillingSubscription ToSubscription(Subscription subscription)
    {
        var state = subscription.State?.Value ?? "unknown";
        return new BillingSubscription(
            subscription.Id!.Value,
            subscription.Reference,
            subscription.Customer?.Id,
            state,
            LiveStates.Contains(state),
            subscription.Product?.Handle,
            subscription.Product?.Name,
            subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents,
            subscription.Currency,
            subscription.Product?.Interval,
            subscription.Product?.IntervalUnit?.Value,
            subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
            subscription.CurrentPeriodEndsAt,
            subscription.ActivatedAt,
            subscription.CreatedAt);
    }
}

/// <summary>The provider may have acted, but gave no usable answer (transport failure, timeout, unreadable 2xx).</summary>
public sealed class MaxioAmbiguousOutcomeException : BillingProviderException
{
    public MaxioAmbiguousOutcomeException(string operation, Exception innerException)
        : base(BillingFailureKind.Unavailable, "The billing provider did not answer in time. Please try again.", innerException: innerException)
    {
        Operation = operation;
    }

    public string Operation { get; }
}
