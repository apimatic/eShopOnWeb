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
using MaxioAdvancedBilling.Requests.Customers;
using MaxioAdvancedBilling.Requests.ProductFamilies;
using MaxioAdvancedBilling.Requests.Subscriptions;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// The one class that talks to the Maxio Advanced Billing SDK. Every SDK failure is translated here into
/// <see cref="BillingProviderException"/>; writes whose outcome is unknown are settled by re-reading the
/// provider by the reference that was sent.
/// </summary>
public sealed class MaxioBillingGateway : ISubscriptionBillingGateway
{
    public const int PlanPageSize = 200;
    private const string PlanCacheKeyPrefix = "maxio-plans:";

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MaxioBillingGateway> _logger;

    public MaxioBillingGateway(MaxioAdvancedBillingClient client, IOptions<MaxioSettings> settings,
        IMemoryCache cache, ILogger<MaxioBillingGateway> logger)
    {
        _client = client;
        _settings = settings.Value;
        _cache = cache;
        _logger = logger;
    }

    public async Task<BillingPlanCatalog> GetPlansAsync(CancellationToken cancellationToken)
    {
        var familyHandle = _settings.ProductFamilyHandle!.Trim();
        var cacheKey = PlanCacheKeyPrefix + familyHandle;
        if (_cache.TryGetValue(cacheKey, out BillingPlanCatalog? cached) && cached is not null)
        {
            return cached;
        }

        var plans = new List<BillingPlan>();
        var truncated = false;
        for (var page = 1; ; page++)
        {
            IReadOnlyList<ProductResponse> batch;
            try
            {
                batch = await _client.ProductFamilies.ListProductsForProductFamily(
                    new ListProductsForProductFamilyRequest
                    {
                        ProductFamilyId = "handle:" + familyHandle,
                        Page = page,
                        PerPage = PlanPageSize,
                        IncludeArchived = false
                    },
                    cancellationToken: cancellationToken);
            }
            catch (ApiException<ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetString(out _))
                {
                    _logger.LogError("Maxio product family {FamilyHandle} was not found (404).", familyHandle);
                    throw new BillingProviderException(BillingFailureKind.Misconfigured,
                        $"The subscription product family '{familyHandle}' was not found in Maxio.", 404, innerException: ex);
                }
                throw FromStatus("ListProductsForProductFamily", ex.StatusCode, RawText(ex.Error), ex);
            }
            catch (SdkException ex)
            {
                throw Translate("ListProductsForProductFamily", ex);
            }

            foreach (var item in batch)
            {
                var product = item.Product;
                if (product?.Id is not int id || string.IsNullOrWhiteSpace(product.Handle) || product.ArchivedAt is not null)
                {
                    continue;
                }
                plans.Add(new BillingPlan(id, product.Handle, product.Name ?? product.Handle, product.Description,
                    product.PriceInCents ?? 0, product.Interval, product.IntervalUnit?.Value));
            }

            if (batch.Count < PlanPageSize)
            {
                break;
            }
            if (page >= _settings.MaxPlanPages)
            {
                // The family may hold more plans than we are allowed to read: say so in the result.
                truncated = true;
                _logger.LogWarning("Plan list for {FamilyHandle} truncated at {Pages} pages.", familyHandle, page);
                break;
            }
        }

        var catalog = new BillingPlanCatalog(plans, truncated);
        if (!truncated && _settings.PlanCacheSeconds > 0)
        {
            _cache.Set(cacheKey, catalog, TimeSpan.FromSeconds(_settings.PlanCacheSeconds));
        }
        _logger.LogInformation("Read {PlanCount} plans for product family {FamilyHandle}.", plans.Count, familyHandle);
        return catalog;
    }

    public async Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(
                new ReadCustomerByReferenceRequest { Reference = reference },
                cancellationToken: cancellationToken);
            return ToCustomer(response.Customer);
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException ex)
        {
            throw Translate("ReadCustomerByReference", ex);
        }
    }

    public async Task<BillingCustomer> CreateCustomerAsync(BillingCustomerProfile profile, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.Customers.CreateCustomer(
                new CreateCustomerOperationRequest
                {
                    Body = new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer
                        {
                            FirstName = profile.FirstName,
                            LastName = profile.LastName,
                            Email = profile.Email,
                            Reference = profile.Reference
                        }
                    }
                },
                cancellationToken: cancellationToken);
            return ToCustomer(response.Customer);
        }
        catch (ApiException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var body))
            {
                // Maxio allows one customer per reference: a 422 may mean another request created it first.
                var existing = await FindCustomerByReferenceAsync(profile.Reference, cancellationToken);
                if (existing is not null)
                {
                    _logger.LogInformation("Customer reference already existed in Maxio; using customer {CustomerId}.", existing.Id);
                    return existing;
                }
                var errors = CustomerErrors(body);
                _logger.LogWarning("Maxio rejected CreateCustomer ({Status}): {Errors}", (int)ex.StatusCode, string.Join("; ", errors));
                throw new BillingProviderException(BillingFailureKind.Rejected,
                    "Maxio rejected the customer: " + string.Join("; ", errors), (int)ex.StatusCode, errors, ex);
            }
            throw FromStatus("CreateCustomer", ex.StatusCode, ex.Error.TryGetRawError(out var raw) ? SafeRead(raw) : null, ex);
        }
        catch (Exception ex) when (IsUnknownWriteOutcome(ex))
        {
            // The customer may have been created. Settle it by the reference we sent.
            var settled = await TrySettleAsync(token => FindCustomerByReferenceAsync(profile.Reference, token));
            if (settled is not null)
            {
                _logger.LogInformation("CreateCustomer went unanswered but customer {CustomerId} exists; using it.", settled.Id);
                return settled;
            }
            _logger.LogWarning(ex, "CreateCustomer outcome unknown for reference {Reference}.", profile.Reference);
            throw new BillingOutcomeUnknownException(KindOf(ex),
                "Maxio did not respond while creating the billing customer.", ex);
        }
        catch (SdkException ex)
        {
            throw Translate("CreateCustomer", ex);
        }
    }

    public async Task<BillingSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        SubscriptionResponse response;
        try
        {
            response = await _client.Subscriptions.FindSubscription(
                new FindSubscriptionRequest { Reference = reference },
                cancellationToken: cancellationToken);
        }
        catch (ApiException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                return null;
            }
            throw FromStatus("FindSubscription", ex.StatusCode, RawText(ex.Error), ex);
        }
        catch (SdkException ex)
        {
            throw Translate("FindSubscription", ex);
        }

        // A 200 without a subscription is not "not found" - it is a response we cannot interpret.
        return response.Subscription is { } subscription
            ? ToSubscription(subscription)
            : throw new BillingProviderException(BillingFailureKind.ProviderError,
                "Maxio returned an empty subscription lookup response.", 200);
    }

    public async Task<BillingSubscription> CreateSubscriptionAsync(int customerId, string planHandle, string reference,
        CancellationToken cancellationToken)
    {
        SubscriptionResponse response;
        try
        {
            response = await _client.Subscriptions.CreateSubscription(
                new CreateSubscriptionOperationRequest
                {
                    Body = new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            CustomerId = customerId,
                            ProductHandle = planHandle,
                            Reference = reference,
                            // No card is captured here, so the default (automatic) would be refused at signup.
                            PaymentCollectionMethod = _settings.CollectionMethod
                        }
                    }
                },
                cancellationToken: cancellationToken);
        }
        catch (ApiException<CreateSubscriptionError> ex) when ((int)ex.StatusCode < 500)
        {
            if (ex.Error.TryGetErrorListResponse1(out var body))
            {
                _logger.LogWarning("Maxio rejected CreateSubscription ({Status}): {Errors}", (int)ex.StatusCode, string.Join("; ", body.Errors));
                throw new BillingProviderException(BillingFailureKind.Rejected,
                    "Maxio rejected the subscription: " + string.Join("; ", body.Errors), (int)ex.StatusCode, body.Errors, ex);
            }
            throw FromStatus("CreateSubscription", ex.StatusCode, ex.Error.TryGetRawError(out var raw) ? SafeRead(raw) : null, ex);
        }
        catch (Exception ex) when (IsUnknownWriteOutcome(ex) || ex is ApiException { StatusCode: >= HttpStatusCode.InternalServerError })
        {
            // No answer, a 5xx, or an unreadable success body: the subscription may exist. The caller settles it.
            _logger.LogWarning(ex, "CreateSubscription outcome unknown for reference {Reference}.", reference);
            throw new BillingOutcomeUnknownException(KindOf(ex), "Maxio did not confirm the subscription.", ex);
        }
        catch (SdkException ex)
        {
            throw Translate("CreateSubscription", ex);
        }

        return response.Subscription is { } subscription
            ? ToSubscription(subscription)
            : throw new BillingOutcomeUnknownException(BillingFailureKind.ProviderError,
                "Maxio accepted the subscription but returned no details.");
    }

    public async Task<IReadOnlyList<BillingSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        IReadOnlyList<SubscriptionResponse> responses;
        try
        {
            responses = await _client.Customers.ListCustomerSubscriptions(
                new ListCustomerSubscriptionsRequest { CustomerId = customerId },
                cancellationToken: cancellationToken);
        }
        catch (SdkException ex)
        {
            throw Translate("ListCustomerSubscriptions", ex);
        }

        return responses
            .Select(r => r.Subscription)
            .OfType<Subscription>()
            .Select(ToSubscription)
            .OrderByDescending(s => s.CreatedAt)
            .ToList();
    }

    private async Task<T?> TrySettleAsync<T>(Func<CancellationToken, Task<T?>> lookup) where T : class
    {
        using var settle = new CancellationTokenSource(TimeSpan.FromSeconds(_settings.SettleBudgetSeconds));
        try
        {
            return await lookup(settle.Token);
        }
        catch (Exception ex) when (ex is BillingProviderException or OperationCanceledException)
        {
            _logger.LogWarning("Settling an unanswered Maxio write failed: {Reason}", ex.Message);
            return null;
        }
    }

    /// <summary>A write that got no usable answer may still have landed at Maxio.</summary>
    private static bool IsUnknownWriteOutcome(Exception ex) =>
        ex is SdkConnectionException or OperationCanceledException
        || ex is ResponseDeserializationException { StatusCode: >= HttpStatusCode.OK and < HttpStatusCode.MultipleChoices };

    private static BillingFailureKind KindOf(Exception ex) => ex switch
    {
        SdkTimeoutException or OperationCanceledException => BillingFailureKind.Timeout,
        SdkConnectionException => BillingFailureKind.Unreachable,
        _ => BillingFailureKind.ProviderError
    };

    /// <summary>The shared ladder for failures that carry no operation-specific typed body.</summary>
    private BillingProviderException Translate(string operation, SdkException ex)
    {
        switch (ex)
        {
            case SdkTimeoutException timeout:
                _logger.LogWarning("Maxio {Operation} timed out after {Timeout}.", operation, timeout.Timeout);
                return new BillingProviderException(BillingFailureKind.Timeout, "Maxio did not respond in time.", innerException: ex);
            case SdkConnectionException:
                _logger.LogWarning(ex, "Maxio {Operation} could not reach Maxio.", operation);
                return new BillingProviderException(BillingFailureKind.Unreachable, "Maxio could not be reached.", innerException: ex);
            case AuthSchemeException:
                _logger.LogError(ex, "Maxio {Operation}: credentials could not be applied.", operation);
                return new BillingProviderException(BillingFailureKind.Misconfigured, "Maxio credentials are not usable.", innerException: ex);
            case ApiException<RawError> api:
                return FromStatus(operation, api.StatusCode, SafeRead(api.Error), ex);
            case ResponseDeserializationException rde when (int)rde.StatusCode is >= 200 and < 300:
                _logger.LogError(ex, "Maxio {Operation} returned a body that could not be read as {Type}.", operation, rde.TargetType.Name);
                return new BillingProviderException(BillingFailureKind.ProviderError,
                    "Maxio returned a response that could not be processed.", (int)rde.StatusCode, innerException: ex);
            case ApiException api:
                // An error status whose body did not match its declared shape: keep the status.
                return FromStatus(operation, api.StatusCode, null, ex);
            default:
                _logger.LogError(ex, "Maxio {Operation} failed.", operation);
                return new BillingProviderException(BillingFailureKind.ProviderError, "Maxio request failed.", innerException: ex);
        }
    }

    private BillingProviderException FromStatus(string operation, HttpStatusCode status, string? detail, Exception ex)
    {
        var code = (int)status;
        switch (code)
        {
            case 401 or 403:
                _logger.LogError("Maxio {Operation} returned {Status}: the configured credentials were rejected.", operation, code);
                return new BillingProviderException(BillingFailureKind.Misconfigured,
                    "Maxio rejected this application's credentials.", code, innerException: ex);
            case 404:
                _logger.LogError("Maxio {Operation} returned 404.", operation);
                return new BillingProviderException(BillingFailureKind.Misconfigured,
                    "A Maxio resource this application depends on was not found.", code, innerException: ex);
            case 429:
                _logger.LogWarning("Maxio {Operation} was rate limited.", operation);
                return new BillingProviderException(BillingFailureKind.ProviderError, "Maxio is rate limiting requests.", code, innerException: ex);
            case >= 400 and < 500:
                _logger.LogWarning("Maxio {Operation} rejected the request ({Status}): {Detail}", operation, code, detail);
                return new BillingProviderException(BillingFailureKind.Rejected, "Maxio rejected the request.", code,
                    detail is null ? null : new[] { detail }, ex);
            default:
                _logger.LogError("Maxio {Operation} failed with {Status}.", operation, code);
                return new BillingProviderException(BillingFailureKind.ProviderError, "Maxio failed to process the request.", code, innerException: ex);
        }
    }

    private static string? RawText(ApiError error) => error.TryGetRawError(out var raw) ? SafeRead(raw) : null;

    private static string? SafeRead(RawError raw)
    {
        var text = raw.ReadAsString();
        return string.IsNullOrWhiteSpace(text) ? null : text.Length <= 500 ? text : text[..500];
    }

    private static IReadOnlyList<string> CustomerErrors(CustomerErrorResponse1 body)
    {
        if (body.Errors is { } errors)
        {
            if (errors.TryGetListOfString(out var list))
            {
                return list;
            }
            if (errors.TryGetCustomerError(out var customerError) && customerError.Customer is { } message)
            {
                return new[] { message };
            }
        }
        return Array.Empty<string>();
    }

    private static BillingCustomer ToCustomer(Customer customer) =>
        customer.Id is int id
            ? new BillingCustomer(id, customer.Reference, customer.Email)
            : throw new BillingProviderException(BillingFailureKind.ProviderError, "Maxio returned a customer without an id.");

    private static BillingSubscription ToSubscription(Subscription subscription)
    {
        if (subscription.Id is not int id)
        {
            throw new BillingProviderException(BillingFailureKind.ProviderError, "Maxio returned a subscription without an id.");
        }
        var product = subscription.Product;
        return new BillingSubscription(
            id,
            subscription.Reference,
            subscription.Customer?.Id,
            product?.Handle,
            product?.Name,
            subscription.ProductPriceInCents ?? product?.PriceInCents,
            subscription.Currency,
            product?.Interval,
            product?.IntervalUnit?.Value,
            subscription.State?.Value,
            subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
            subscription.CurrentPeriodEndsAt,
            subscription.CreatedAt);
    }
}
