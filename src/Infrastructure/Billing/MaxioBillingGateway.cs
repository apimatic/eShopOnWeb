using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
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
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Maxio Advanced Billing implementation of the billing gateway: the single place where SDK calls are made,
/// bounded by a per-call budget, and where every SDK failure is translated into the application's billing exceptions.
/// </summary>
public class MaxioBillingGateway : ISubscriptionBillingGateway
{
    public const int PlanPageSize = 200;
    public const int MaxPlanPages = 10;

    private const string PlanCacheKeyPrefix = "maxio:plans:";
    private const int MaxLoggedErrorBodyLength = 500;

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MaxioBillingGateway> _logger;
    private readonly CollectionMethod _paymentCollectionMethod;

    public MaxioBillingGateway(MaxioAdvancedBillingClient client,
        IOptions<MaxioSettings> settings,
        IMemoryCache cache,
        ILogger<MaxioBillingGateway> logger)
    {
        _client = client;
        _settings = settings.Value;
        _cache = cache;
        _logger = logger;
        // Validated at startup (MaxioSettingsValidator), so the value is always a declared member.
        _paymentCollectionMethod = CollectionMethod.TryGetKnownValue(_settings.PaymentCollectionMethod, out var collectionMethod)
            ? collectionMethod
            : CollectionMethod.Remittance;
    }

    public async Task<SubscriptionPlanCatalog> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var familyHandle = _settings.ProductFamilyHandle!;
        var cacheKey = PlanCacheKeyPrefix + familyHandle;
        if (_cache.TryGetValue(cacheKey, out SubscriptionPlanCatalog? cached) && cached is not null)
        {
            return cached;
        }

        var plans = new List<SubscriptionPlan>();
        var truncated = false;
        for (var page = 1; ; page++)
        {
            if (page > MaxPlanPages)
            {
                truncated = true;
                _logger.LogWarning("Plan listing for product family {ProductFamilyHandle} stopped at the {MaxPages}-page cap; the catalog is partial.",
                    familyHandle, MaxPlanPages);
                break;
            }

            var items = await ListPlanPageAsync(familyHandle, page, cancellationToken);
            plans.AddRange(items.Select(item => ToPlan(item.Product)).OfType<SubscriptionPlan>());
            if (items.Count < PlanPageSize)
            {
                break;
            }
        }

        var catalog = new SubscriptionPlanCatalog(plans, truncated);
        if (_settings.PlanCacheDuration > TimeSpan.Zero)
        {
            _cache.Set(cacheKey, catalog, _settings.PlanCacheDuration);
        }

        return catalog;
    }

    public async Task<BillingCustomerRecord?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        const string operation = "ReadCustomerByReference";
        try
        {
            var response = await BoundedAsync(operation, isWrite: false,
                ct => _client.Customers.ReadCustomerByReference(new ReadCustomerByReferenceRequest { Reference = reference }, cancellationToken: ct),
                cancellationToken);
            var customer = response.Customer;
            if (customer?.Id is not int id)
            {
                throw Unreadable(operation, isWrite: false);
            }

            return new BillingCustomerRecord(id, customer.Reference);
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException ex)
        {
            throw Translate(operation, ex, isWrite: false);
        }
    }

    public async Task<BillingCustomerRecord> CreateCustomerAsync(NewBillingCustomer customer, CancellationToken cancellationToken = default)
    {
        const string operation = "CreateCustomer";
        var request = new CreateCustomerOperationRequest
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
        };

        try
        {
            var response = await BoundedAsync(operation, isWrite: true,
                ct => _client.Customers.CreateCustomer(request, cancellationToken: ct),
                cancellationToken);
            var created = response.Customer;
            if (created?.Id is not int id)
            {
                throw Unreadable(operation, isWrite: true);
            }

            return new BillingCustomerRecord(id, created.Reference);
        }
        catch (ApiException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var body))
            {
                throw Rejected(operation, ex.StatusCode, CustomerErrors(body), ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromStatus(operation, ex.StatusCode, isWrite: true, ReadErrors(raw), ex);
            }

            throw FromStatus(operation, ex.StatusCode, isWrite: true, Array.Empty<string>(), ex);
        }
        catch (SdkException ex)
        {
            throw Translate(operation, ex, isWrite: true);
        }
    }

    public async Task<BillingSubscription> CreateSubscriptionAsync(NewBillingSubscription subscription, CancellationToken cancellationToken = default)
    {
        const string operation = "CreateSubscription";
        var request = new CreateSubscriptionOperationRequest
        {
            Body = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = subscription.PlanHandle,
                    CustomerId = subscription.BillingCustomerId,
                    Reference = subscription.Reference,
                    PaymentCollectionMethod = _paymentCollectionMethod
                }
            }
        };

        try
        {
            var response = await BoundedAsync(operation, isWrite: true,
                ct => _client.Subscriptions.CreateSubscription(request, cancellationToken: ct),
                cancellationToken);
            return ToSubscription(response.Subscription) ?? throw Unreadable(operation, isWrite: true);
        }
        catch (ApiException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var body))
            {
                throw Rejected(operation, ex.StatusCode, body.Errors ?? Array.Empty<string>(), ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromStatus(operation, ex.StatusCode, isWrite: true, ReadErrors(raw), ex);
            }

            throw FromStatus(operation, ex.StatusCode, isWrite: true, Array.Empty<string>(), ex);
        }
        catch (SdkException ex)
        {
            throw Translate(operation, ex, isWrite: true);
        }
    }

    public async Task<BillingSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        const string operation = "FindSubscription";
        try
        {
            var response = await BoundedAsync(operation, isWrite: false,
                ct => _client.Subscriptions.FindSubscription(new FindSubscriptionRequest { Reference = reference }, cancellationToken: ct),
                cancellationToken);
            // A 2xx without a readable subscription is not an absence — never let it pass as "not found".
            return ToSubscription(response.Subscription) ?? throw Unreadable(operation, isWrite: false);
        }
        catch (ApiException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                return null;
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromStatus(operation, ex.StatusCode, isWrite: false, ReadErrors(raw), ex);
            }

            throw FromStatus(operation, ex.StatusCode, isWrite: false, Array.Empty<string>(), ex);
        }
        catch (SdkException ex)
        {
            throw Translate(operation, ex, isWrite: false);
        }
    }

    public async Task<IReadOnlyList<BillingSubscription>> ListCustomerSubscriptionsAsync(int billingCustomerId, CancellationToken cancellationToken = default)
    {
        const string operation = "ListCustomerSubscriptions";
        try
        {
            var response = await BoundedAsync(operation, isWrite: false,
                ct => _client.Customers.ListCustomerSubscriptions(new ListCustomerSubscriptionsRequest { CustomerId = billingCustomerId }, cancellationToken: ct),
                cancellationToken);
            return response.Select(item => ToSubscription(item?.Subscription)).OfType<BillingSubscription>().ToList();
        }
        catch (SdkException ex)
        {
            throw Translate(operation, ex, isWrite: false);
        }
    }

    private async Task<IReadOnlyList<ProductResponse>> ListPlanPageAsync(string familyHandle, int page, CancellationToken cancellationToken)
    {
        const string operation = "ListProductsForProductFamily";
        try
        {
            return await BoundedAsync(operation, isWrite: false,
                ct => _client.ProductFamilies.ListProductsForProductFamily(new ListProductsForProductFamilyRequest
                {
                    ProductFamilyId = "handle:" + familyHandle,
                    Page = page,
                    PerPage = PlanPageSize,
                    IncludeArchived = false
                }, cancellationToken: ct),
                cancellationToken);
        }
        catch (ApiException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out var notFound))
            {
                // Our configuration names a family the site does not have — not something the caller can fix.
                _logger.LogError("Maxio product family {ProductFamilyHandle} was not found (HTTP {Status}): {Detail}",
                    familyHandle, (int)ex.StatusCode, Truncate(notFound));
                throw new BillingProviderUnavailableException("Subscription plans are not available right now.", (int)ex.StatusCode, ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromStatus(operation, ex.StatusCode, isWrite: false, ReadErrors(raw), ex);
            }

            throw FromStatus(operation, ex.StatusCode, isWrite: false, Array.Empty<string>(), ex);
        }
        catch (SdkException ex)
        {
            throw Translate(operation, ex, isWrite: false);
        }
    }

    /// <summary>
    /// The one home of the per-call budget: every SDK call runs under it, linked to the caller's token.
    /// The budget firing on a write leaves its outcome unknown.
    /// </summary>
    private async Task<T> BoundedAsync<T>(string operation, bool isWrite, Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(_settings.CallBudget);
        try
        {
            return await call(budget.Token);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Maxio {Operation} exceeded its {Budget} budget.", operation, _settings.CallBudget);
            return isWrite
                ? throw new BillingOutcomeUnknownException("The billing system did not answer in time.", null, ex)
                : throw new BillingProviderUnavailableException("The billing system did not answer in time.", null, ex);
        }
    }

    /// <summary>Translates the SDK failures that carry no typed body. Typed bodies are read in each operation's own catch.</summary>
    private Exception Translate(string operation, SdkException exception, bool isWrite)
    {
        switch (exception)
        {
            case ApiException<RawError> apiException:
                return FromStatus(operation, apiException.StatusCode, isWrite, ReadErrors(apiException.Error), apiException);

            case ResponseDeserializationException deserialization when IsSuccess(deserialization.StatusCode):
                // The call may have succeeded but its result cannot be read.
                _logger.LogError(deserialization, "Maxio {Operation} returned HTTP {Status} with a body that could not be read as {TargetType}.",
                    operation, (int)deserialization.StatusCode, deserialization.TargetType.Name);
                return isWrite
                    ? new BillingOutcomeUnknownException("The billing system's response could not be read.", (int)deserialization.StatusCode, deserialization)
                    : new BillingProviderUnavailableException("The billing system's response could not be read.", (int)deserialization.StatusCode, deserialization);

            case ResponseDeserializationException deserialization:
                // The provider refused the request; only the error detail was lost.
                return FromStatus(operation, deserialization.StatusCode, isWrite, Array.Empty<string>(), deserialization);

            case ApiException apiException:
                return FromStatus(operation, apiException.StatusCode, isWrite, Array.Empty<string>(), apiException);

            case SdkConnectionException connection:
                // Includes SdkTimeoutException. On a write the request may still have landed.
                _logger.LogError(connection, "Maxio {Operation} got no usable response ({Failure}).", operation, connection.GetType().Name);
                return isWrite
                    ? new BillingOutcomeUnknownException("The billing system could not be reached.", null, connection)
                    : new BillingProviderUnavailableException("The billing system could not be reached.", null, connection);

            case AuthSchemeException auth:
                _logger.LogCritical(auth, "Maxio credentials could not be applied for {Operation}.", operation);
                return new BillingProviderUnavailableException("The billing system is unavailable.", null, auth);

            default:
                _logger.LogError(exception, "Maxio {Operation} failed.", operation);
                return new BillingProviderUnavailableException("The billing system is unavailable.", null, exception);
        }
    }

    private Exception FromStatus(string operation, HttpStatusCode status, bool isWrite, IReadOnlyList<string> errors, Exception inner)
    {
        var code = (int)status;
        if (code is 401 or 403)
        {
            // Our credentials, not the caller's fault.
            _logger.LogCritical("Maxio rejected this application's credentials for {Operation} (HTTP {Status}).", operation, code);
            return new BillingProviderUnavailableException("The billing system is unavailable.", code, inner);
        }

        if (code == 429)
        {
            _logger.LogWarning("Maxio rate-limited {Operation} (HTTP 429).", operation);
            return new BillingProviderUnavailableException("The billing system is busy. Please retry shortly.", code, inner);
        }

        if (code is >= 400 and < 500)
        {
            return Rejected(operation, status, errors, inner);
        }

        _logger.LogError(inner, "Maxio {Operation} failed with HTTP {Status}.", operation, code);
        return isWrite
            ? new BillingOutcomeUnknownException("The billing system failed while processing the request.", code, inner)
            : new BillingProviderUnavailableException("The billing system is unavailable.", code, inner);
    }

    private BillingRequestRejectedException Rejected(string operation, HttpStatusCode status, IReadOnlyList<string> errors, Exception inner)
    {
        var details = errors.Where(e => !string.IsNullOrWhiteSpace(e)).ToList();
        _logger.LogWarning("Maxio rejected {Operation} with HTTP {Status}: {Errors}",
            operation, (int)status, Truncate(string.Join("; ", details)));
        var message = details.Count > 0
            ? "The billing system rejected the request: " + string.Join("; ", details)
            : "The billing system rejected the request.";
        return new BillingRequestRejectedException(message, details, (int)status, inner);
    }

    private BillingProviderException Unreadable(string operation, bool isWrite)
    {
        _logger.LogError("Maxio {Operation} succeeded but its response did not carry the expected record.", operation);
        return isWrite
            ? new BillingOutcomeUnknownException("The billing system's response could not be read.")
            : new BillingProviderUnavailableException("The billing system's response could not be read.");
    }

    private static IReadOnlyList<string> CustomerErrors(CustomerErrorResponse1 body)
    {
        if (body.Errors is null)
        {
            return Array.Empty<string>();
        }

        if (body.Errors.TryGetCustomerError(out var customerError) && !string.IsNullOrWhiteSpace(customerError.Customer))
        {
            return new[] { customerError.Customer };
        }

        if (body.Errors.TryGetListOfString(out var list))
        {
            return list;
        }

        return Array.Empty<string>();
    }

    /// <summary>Reads Maxio's usual <c>{"errors": [...]}</c> shape from an untyped body; anything else yields no detail.</summary>
    private static IReadOnlyList<string> ReadErrors(RawError raw)
    {
        string text;
        try
        {
            text = raw.ReadAsString();
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("errors", out var errors))
            {
                return errors.ValueKind switch
                {
                    JsonValueKind.Array => errors.EnumerateArray()
                        .Where(e => e.ValueKind == JsonValueKind.String)
                        .Select(e => e.GetString()!)
                        .ToList(),
                    JsonValueKind.String => new[] { errors.GetString()! },
                    _ => Array.Empty<string>()
                };
            }
        }
        catch (JsonException)
        {
            // not JSON (a gateway or proxy page) — no detail to surface
        }

        return Array.Empty<string>();
    }

    private static SubscriptionPlan? ToPlan(Product? product)
    {
        if (product is null || string.IsNullOrWhiteSpace(product.Handle) || product.ArchivedAt is not null)
        {
            return null;
        }

        return new SubscriptionPlan(
            product.Id,
            product.Handle,
            string.IsNullOrWhiteSpace(product.Name) ? product.Handle : product.Name,
            product.Description,
            product.PriceInCents ?? 0,
            product.Interval,
            product.IntervalUnit?.Value);
    }

    private static BillingSubscription? ToSubscription(Subscription? subscription)
    {
        if (subscription?.Id is not int id)
        {
            return null;
        }

        return new BillingSubscription(
            id,
            subscription.Reference,
            subscription.State?.Value ?? "unknown",
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

    private static bool IsSuccess(HttpStatusCode status) => (int)status is >= 200 and < 300;

    private static string Truncate(string? value) =>
        value is null ? string.Empty : value.Length <= MaxLoggedErrorBodyLength ? value : value[..MaxLoggedErrorBodyLength] + "…";
}
