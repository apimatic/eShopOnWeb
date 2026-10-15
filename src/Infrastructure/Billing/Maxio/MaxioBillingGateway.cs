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

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// The only code that talks to Maxio. Every SDK failure is translated here into <see cref="BillingProviderException"/>,
/// every call is bounded by <see cref="CallBudget"/>, and a write whose outcome is unknown is settled by re-reading
/// Maxio by the reference it carried before anything is reported.
/// </summary>
public class MaxioBillingGateway : ISubscriptionBillingGateway
{
    /// <summary>Total budget for one gateway call, retries included (linked to the caller's token).</summary>
    public static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(25);

    /// <summary>Budget for the re-read that settles a write whose outcome is unknown; independent of the caller's token.</summary>
    private static readonly TimeSpan SettleBudget = TimeSpan.FromSeconds(15);

    private static readonly TimeSpan PlanCacheDuration = TimeSpan.FromSeconds(60);
    private const int PlansPerPage = 200;
    private const int MaxPlanPages = 10;

    private readonly MaxioAdvancedBillingClient _client;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MaxioBillingGateway> _logger;
    private readonly string _productFamilyHandle;
    private readonly CollectionMethod _paymentCollectionMethod;

    public MaxioBillingGateway(MaxioAdvancedBillingClient client, IOptions<MaxioOptions> options, IMemoryCache cache,
        ILogger<MaxioBillingGateway> logger)
    {
        _client = client;
        _cache = cache;
        _logger = logger;
        _productFamilyHandle = options.Value.ProductFamilyHandle!.Trim();
        // Validated at startup (MaxioOptionsValidator), so the lookup cannot fail here.
        CollectionMethod.TryGetKnownValue(
            string.IsNullOrWhiteSpace(options.Value.PaymentCollectionMethod)
                ? MaxioOptions.DefaultPaymentCollectionMethod
                : options.Value.PaymentCollectionMethod.Trim(),
            out var collectionMethod);
        _paymentCollectionMethod = collectionMethod ?? CollectionMethod.Remittance;
    }

    public string ProductFamilyHandle => _productFamilyHandle;

    public async Task<PlanCatalog> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var cacheKey = $"maxio-plans:{_productFamilyHandle}";
        if (_cache.TryGetValue(cacheKey, out PlanCatalog? cached) && cached is not null)
        {
            return cached;
        }

        var catalog = await LoadPlansAsync(cancellationToken);
        _cache.Set(cacheKey, catalog, PlanCacheDuration);
        return catalog;
    }

    public async Task<int?> FindCustomerIdAsync(string customerReference, CancellationToken cancellationToken = default)
    {
        var response = await ExecuteAsync("ReadCustomerByReference", async ct =>
        {
            try
            {
                return await _client.Customers.ReadCustomerByReference(
                    new ReadCustomerByReferenceRequest { Reference = customerReference },
                    cancellationToken: ct);
            }
            catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
        }, cancellationToken);

        return response?.Customer?.Id;
    }

    public async Task<int> EnsureCustomerAsync(BillingCustomer customer, CancellationToken cancellationToken = default)
    {
        var existingId = await FindCustomerIdAsync(customer.Reference, cancellationToken);
        if (existingId is not null)
        {
            return existingId.Value;
        }

        var request = new CreateCustomerOperationRequest
        {
            Body = new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = customer.FirstName,
                    LastName = customer.LastName,
                    Email = customer.Email,
                    // Maxio allows one customer per reference: this is what makes customer creation idempotent.
                    Reference = customer.Reference,
                }
            }
        };

        try
        {
            var created = await ExecuteWriteAsync(ct => _client.Customers.CreateCustomer(request, cancellationToken: ct), cancellationToken);
            if (created.Customer?.Id is int id)
            {
                _logger.LogInformation("Created Maxio customer {CustomerId} for reference {CustomerReference}", id, customer.Reference);
                return id;
            }
            return await SettleCustomerAsync(customer.Reference, "the create response carried no customer id", cancellationToken);
        }
        catch (ApiException<CreateCustomerError> ex) when (IsDefinitiveRefusal(ex.StatusCode))
        {
            var errors = ex.Error.TryGetCustomerErrorResponse1(out var body) ? ReadCustomerErrors(body)
                : ex.Error.TryGetRawError(out var raw) ? ReadErrors(raw)
                : Array.Empty<string>();

            // A 422 may mean another request created this reference first: adopt that customer.
            var raced = await TryFindCustomerQuietlyAsync(customer.Reference);
            if (raced is not null)
            {
                return raced.Value;
            }
            _logger.LogWarning("Maxio refused customer creation for {CustomerReference}: HTTP {StatusCode} {Errors}",
                customer.Reference, (int)ex.StatusCode, string.Join("; ", errors));
            throw FromStatus("create the billing customer", ex.StatusCode, errors, ex);
        }
        catch (ApiException<CreateCustomerError> ex)
        {
            // 5xx/408: the customer may have been created.
            return await SettleCustomerAsync(customer.Reference, $"HTTP {(int)ex.StatusCode}", cancellationToken, ex);
        }
        catch (ResponseDeserializationException ex) when (IsDefinitiveRefusal(ex.StatusCode))
        {
            throw FromStatus("create the billing customer", ex.StatusCode, null, ex);
        }
        catch (Exception ex) when (IsUnknownOutcome(ex, cancellationToken))
        {
            return await SettleCustomerAsync(customer.Reference, ex.GetType().Name, cancellationToken, ex);
        }
        catch (AuthSchemeException ex)
        {
            throw Misconfigured(ex);
        }
    }

    public async Task<SubscriptionCreationResult> CreateSubscriptionAsync(int customerId, string planHandle,
        string subscriptionReference, CancellationToken cancellationToken = default)
    {
        var request = new CreateSubscriptionOperationRequest
        {
            Body = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = planHandle,
                    CustomerId = customerId,
                    // No card is captured by this storefront, so by default Maxio invoices the shopper (remittance).
                    PaymentCollectionMethod = _paymentCollectionMethod,
                    // Our own reference: the key we search by when the outcome of this write is unknown.
                    Reference = subscriptionReference,
                }
            }
        };

        try
        {
            var response = await ExecuteWriteAsync(ct => _client.Subscriptions.CreateSubscription(request, cancellationToken: ct), cancellationToken);
            var created = Map(response.Subscription);
            if (created is not null)
            {
                return SubscriptionCreationResult.Created(created);
            }
            return await SettleSubscriptionAsync(subscriptionReference, "the create response carried no subscription");
        }
        catch (ApiException<CreateSubscriptionError> ex) when (IsDefinitiveRefusal(ex.StatusCode))
        {
            var errors = ex.Error.TryGetErrorListResponse1(out var body) ? body.Errors
                : ex.Error.TryGetRawError(out var raw) ? ReadErrors(raw)
                : Array.Empty<string>();

            // A refusal can be Maxio rejecting a reference an earlier attempt already used: if it exists, it is ours.
            var existing = await TryFindSubscriptionQuietlyAsync(subscriptionReference);
            if (existing is not null)
            {
                return SubscriptionCreationResult.Created(existing);
            }
            _logger.LogWarning("Maxio refused subscription {SubscriptionReference} on plan {PlanHandle}: HTTP {StatusCode} {Errors}",
                subscriptionReference, planHandle, (int)ex.StatusCode, string.Join("; ", errors));
            throw FromStatus("create the subscription", ex.StatusCode, errors, ex);
        }
        catch (ApiException<CreateSubscriptionError> ex)
        {
            // 5xx/408: Maxio may have created the subscription before failing.
            return await SettleSubscriptionAsync(subscriptionReference, $"HTTP {(int)ex.StatusCode}", ex);
        }
        catch (ResponseDeserializationException ex) when (IsDefinitiveRefusal(ex.StatusCode))
        {
            var existing = await TryFindSubscriptionQuietlyAsync(subscriptionReference);
            if (existing is not null)
            {
                return SubscriptionCreationResult.Created(existing);
            }
            throw FromStatus("create the subscription", ex.StatusCode, null, ex);
        }
        catch (Exception ex) when (IsUnknownOutcome(ex, cancellationToken))
        {
            return await SettleSubscriptionAsync(subscriptionReference, ex.GetType().Name, ex);
        }
        catch (AuthSchemeException ex)
        {
            throw Misconfigured(ex);
        }
    }

    public async Task<BillingSubscription?> FindSubscriptionAsync(string subscriptionReference, CancellationToken cancellationToken = default)
    {
        var response = await ExecuteAsync("FindSubscription", async ct =>
        {
            try
            {
                return await _client.Subscriptions.FindSubscription(
                    new FindSubscriptionRequest { Reference = subscriptionReference },
                    cancellationToken: ct);
            }
            catch (ApiException<FindSubscriptionError> ex)
            {
                if (ex.Error.TryGetNoContent(out _))
                {
                    return null; // 404: Maxio holds no subscription with this reference.
                }
                var errors = ex.Error.TryGetRawError(out var raw) ? ReadErrors(raw) : null;
                throw FromStatus("look up the subscription", ex.StatusCode, errors, ex);
            }
        }, cancellationToken);

        return Map(response?.Subscription);
    }

    public async Task<BillingSubscription?> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        var response = await ExecuteAsync("ReadSubscription", async ct =>
        {
            try
            {
                return await _client.Subscriptions.ReadSubscription(
                    new ReadSubscriptionRequest { SubscriptionId = subscriptionId },
                    cancellationToken: ct);
            }
            catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
        }, cancellationToken);

        return Map(response?.Subscription);
    }

    public async Task<IReadOnlyList<BillingSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var response = await ExecuteAsync("ListCustomerSubscriptions",
            ct => _client.Customers.ListCustomerSubscriptions(new ListCustomerSubscriptionsRequest { CustomerId = customerId }, cancellationToken: ct),
            cancellationToken);

        return response
            .Select(r => Map(r?.Subscription))
            .Where(s => s is not null)
            .Select(s => s!)
            .ToList();
    }

    private async Task<PlanCatalog> LoadPlansAsync(CancellationToken cancellationToken)
    {
        var plans = new List<BillingPlan>();
        var truncated = false;

        for (var page = 1; ; page++)
        {
            var currentPage = page;
            var batch = await ExecuteAsync("ListProductsForProductFamily", async ct =>
            {
                try
                {
                    return await _client.ProductFamilies.ListProductsForProductFamily(
                        new ListProductsForProductFamilyRequest
                        {
                            ProductFamilyId = "handle:" + _productFamilyHandle,
                            Page = currentPage,
                            PerPage = PlansPerPage,
                        },
                        cancellationToken: ct);
                }
                catch (ApiException<ListProductsForProductFamilyError> ex)
                {
                    if (ex.Error.TryGetString(out _))
                    {
                        // 404: the configured product family does not exist on this site.
                        _logger.LogError("Maxio product family {ProductFamilyHandle} was not found (HTTP {StatusCode}); check Maxio:ProductFamilyHandle",
                            _productFamilyHandle, (int)ex.StatusCode);
                        throw new BillingProviderException("Subscription plans are not available right now.",
                            BillingFailureKind.Misconfigured, (int)ex.StatusCode, innerException: ex);
                    }
                    var errors = ex.Error.TryGetRawError(out var raw) ? ReadErrors(raw) : null;
                    throw FromStatus("list subscription plans", ex.StatusCode, errors, ex);
                }
            }, cancellationToken);

            foreach (var product in batch.Select(r => r?.Product))
            {
                if (product?.Handle is null || product.ArchivedAt is not null)
                {
                    continue;
                }
                plans.Add(new BillingPlan(product.Id, product.Handle, product.Name ?? product.Handle, product.Description,
                    product.PriceInCents ?? 0, product.Interval, product.IntervalUnit?.Value));
            }

            if (batch.Count < PlansPerPage)
            {
                break;
            }
            if (page >= MaxPlanPages)
            {
                truncated = true;
                _logger.LogWarning("Plan listing for {ProductFamilyHandle} stopped at {MaxPages} pages; result is truncated",
                    _productFamilyHandle, MaxPlanPages);
                break;
            }
        }

        return new PlanCatalog(_productFamilyHandle, plans, truncated);
    }

    /// <summary>
    /// Runs a read within <see cref="CallBudget"/> and translates every SDK failure. Typed (Case A) errors are handled by
    /// the caller's lambda, where the concrete error type is known.
    /// </summary>
    private async Task<T> ExecuteAsync<T>(string operation, Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(CallBudget);
        try
        {
            return await call(budget.Token);
        }
        catch (ApiException<RawError> ex)
        {
            throw FromStatus(Describe(operation), ex.StatusCode, ReadErrors(ex.Error), ex);
        }
        catch (ResponseDeserializationException ex)
        {
            // Never treat an unreadable body as "not found": a 2xx we cannot read is an unknown, not an absence.
            _logger.LogError(ex, "Maxio {Operation} returned an unreadable body (HTTP {StatusCode})", operation, (int)ex.StatusCode);
            throw FromStatus(Describe(operation), IsDefinitiveRefusal(ex.StatusCode) ? ex.StatusCode : HttpStatusCode.BadGateway, null, ex);
        }
        catch (SdkConnectionException ex)
        {
            _logger.LogWarning("Maxio {Operation} could not complete: {Failure}", operation, ex.GetType().Name);
            throw Unavailable(ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Maxio {Operation} exceeded its {Budget} budget", operation, CallBudget);
            throw Unavailable(ex);
        }
        catch (AuthSchemeException ex)
        {
            throw Misconfigured(ex);
        }
    }

    /// <summary>Runs a write within <see cref="CallBudget"/>. Failures propagate untranslated so the caller can settle unknown outcomes.</summary>
    private static async Task<T> ExecuteWriteAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(CallBudget);
        return await call(budget.Token);
    }

    /// <summary>A failure after which a write may or may not have landed. Caller cancellation is excluded: it propagates.</summary>
    private static bool IsUnknownOutcome(Exception ex, CancellationToken callerToken) => ex switch
    {
        SdkConnectionException => true, // includes SdkTimeoutException
        ResponseDeserializationException => true, // a 2xx/5xx body we could not read (4xx is handled before)
        OperationCanceledException => !callerToken.IsCancellationRequested, // our own budget elapsed
        _ => false
    };

    /// <summary>4xx other than 408 means Maxio refused the request without acting on it.</summary>
    private static bool IsDefinitiveRefusal(HttpStatusCode status) => (int)status is >= 400 and < 500 and not 408;

    private async Task<SubscriptionCreationResult> SettleSubscriptionAsync(string subscriptionReference, string cause, Exception? ex = null)
    {
        _logger.LogWarning("Maxio subscription create {SubscriptionReference} has an unknown outcome ({Cause}); re-reading by reference",
            subscriptionReference, cause);
        var found = await TryFindSubscriptionQuietlyAsync(subscriptionReference);
        if (found is not null)
        {
            _logger.LogInformation("Subscription {SubscriptionReference} was created at Maxio as {SubscriptionId}", subscriptionReference, found.Id);
            return SubscriptionCreationResult.Created(found);
        }
        _logger.LogWarning(ex, "Subscription {SubscriptionReference} not found after an unknown outcome; left for reconciliation", subscriptionReference);
        return SubscriptionCreationResult.Unknown();
    }

    private async Task<int> SettleCustomerAsync(string customerReference, string cause, CancellationToken cancellationToken, Exception? ex = null)
    {
        _logger.LogWarning("Maxio customer create for {CustomerReference} has an unknown outcome ({Cause}); re-reading by reference",
            customerReference, cause);
        var found = await TryFindCustomerQuietlyAsync(customerReference);
        if (found is not null)
        {
            return found.Value;
        }
        // Not found (yet): customer creation is idempotent by reference, so the next attempt re-reads before creating.
        cancellationToken.ThrowIfCancellationRequested();
        throw new BillingProviderException("The billing provider did not confirm the customer. Please try again.",
            BillingFailureKind.Unavailable, innerException: ex);
    }

    private async Task<BillingSubscription?> TryFindSubscriptionQuietlyAsync(string subscriptionReference)
    {
        using var settle = new CancellationTokenSource(SettleBudget);
        try
        {
            return await FindSubscriptionAsync(subscriptionReference, settle.Token);
        }
        catch (Exception ex) when (ex is BillingProviderException or OperationCanceledException)
        {
            _logger.LogWarning("Re-reading subscription {SubscriptionReference} failed: {Failure}", subscriptionReference, ex.Message);
            return null;
        }
    }

    private async Task<int?> TryFindCustomerQuietlyAsync(string customerReference)
    {
        using var settle = new CancellationTokenSource(SettleBudget);
        try
        {
            return await FindCustomerIdAsync(customerReference, settle.Token);
        }
        catch (Exception ex) when (ex is BillingProviderException or OperationCanceledException)
        {
            _logger.LogWarning("Re-reading customer {CustomerReference} failed: {Failure}", customerReference, ex.Message);
            return null;
        }
    }

    private static BillingSubscription? Map(Subscription? subscription)
    {
        if (subscription?.Id is not int id)
        {
            return null;
        }

        return new BillingSubscription(
            id,
            subscription.Reference,
            subscription.Customer?.Id,
            // Open enum: an undeclared state still round-trips as its wire value.
            subscription.State?.Value ?? "unknown",
            subscription.Product?.Handle,
            subscription.Product?.Name,
            subscription.Product?.ProductFamily?.Handle,
            subscription.ProductPriceInCents,
            subscription.CurrentBillingAmountInCents,
            subscription.Currency,
            subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
            subscription.CurrentPeriodEndsAt,
            subscription.ActivatedAt,
            subscription.CreatedAt);
    }

    private static BillingProviderException FromStatus(string action, HttpStatusCode status, IReadOnlyList<string>? errors, Exception inner)
    {
        var code = (int)status;
        return code switch
        {
            401 or 403 => new BillingProviderException("The billing provider is not available right now.",
                BillingFailureKind.Misconfigured, code, null, inner),
            408 or 429 => new BillingProviderException("The billing provider is busy. Please try again shortly.",
                BillingFailureKind.Unavailable, code, null, inner),
            >= 400 and < 500 => new BillingProviderException($"The billing provider could not {action}.",
                BillingFailureKind.Rejected, code, errors, inner),
            _ => new BillingProviderException("The billing provider is not available right now.",
                BillingFailureKind.Unavailable, code, null, inner),
        };
    }

    private static BillingProviderException Unavailable(Exception inner) =>
        new("The billing provider is not available right now.", BillingFailureKind.Unavailable, innerException: inner);

    private static BillingProviderException Misconfigured(Exception inner) =>
        new("The billing provider is not available right now.", BillingFailureKind.Misconfigured, innerException: inner);

    private static string Describe(string operation) => operation switch
    {
        "ReadCustomerByReference" => "find the billing customer",
        "FindSubscription" or "ReadSubscription" => "look up the subscription",
        "ListCustomerSubscriptions" => "list subscriptions",
        "ListProductsForProductFamily" => "list subscription plans",
        _ => "complete the request"
    };

    private static IReadOnlyList<string> ReadCustomerErrors(CustomerErrorResponse1 body)
    {
        if (body.Errors is null)
        {
            return Array.Empty<string>();
        }
        if (body.Errors.TryGetListOfString(out var list))
        {
            return list;
        }
        if (body.Errors.TryGetCustomerError(out var single) && !string.IsNullOrWhiteSpace(single.Customer))
        {
            return new[] { single.Customer };
        }
        return Array.Empty<string>();
    }

    /// <summary>Best-effort read of Maxio's <c>{"errors": …}</c> body; the body may not be JSON at all.</summary>
    private static IReadOnlyList<string> ReadErrors(RawError raw)
    {
        try
        {
            using var document = JsonDocument.Parse(raw.ReadAsString());
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("errors", out var errors))
            {
                return Array.Empty<string>();
            }
            return errors.ValueKind switch
            {
                JsonValueKind.Array => errors.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList(),
                JsonValueKind.Object => errors.EnumerateObject().SelectMany(p => p.Value.ValueKind == JsonValueKind.Array
                    ? p.Value.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => $"{p.Name} {e.GetString()}")
                    : p.Value.ValueKind == JsonValueKind.String ? new[] { $"{p.Name} {p.Value.GetString()}" } : Array.Empty<string>()).ToList(),
                JsonValueKind.String => new[] { errors.GetString()! },
                _ => Array.Empty<string>()
            };
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }
}
