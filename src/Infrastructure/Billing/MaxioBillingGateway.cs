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
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// The only class that talks to the Maxio SDK. Every call is bounded by one budget, every SDK failure is translated
/// into <see cref="BillingException"/> with a caller-safe message, and a write whose connection fails is settled here —
/// by looking it up by the reference it was sent with — before an outcome is reported.
/// </summary>
public sealed class MaxioBillingGateway : ISubscriptionBillingGateway
{
    /// <summary>Whole-call budget for one SDK call, retries included.</summary>
    public static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(20);

    /// <summary>Budget for the re-read that settles a write whose outcome is unknown.</summary>
    public static readonly TimeSpan SettleBudget = TimeSpan.FromSeconds(10);

    public const int PlanPageSize = 200;
    public const int MaxPlanPages = 5;
    private static readonly TimeSpan PlanCacheTtl = TimeSpan.FromMinutes(5);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MaxioBillingGateway> _logger;

    public MaxioBillingGateway(MaxioAdvancedBillingClient client,
        IOptions<MaxioSettings> settings,
        IMemoryCache cache,
        ILogger<MaxioBillingGateway> logger)
    {
        _client = client;
        _cache = cache;
        _logger = logger;
        ProductFamilyHandle = settings.Value.ProductFamilyHandle!;
    }

    public string ProductFamilyHandle { get; }

    public async Task<PlanCatalog> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var cacheKey = "maxio:plans:" + ProductFamilyHandle;
        if (_cache.TryGetValue(cacheKey, out PlanCatalog? cached) && cached is not null)
        {
            return cached;
        }

        var plans = new List<SubscriptionPlan>();
        var truncated = false;
        for (var page = 1; page <= MaxPlanPages; page++)
        {
            IReadOnlyList<ProductResponse> batch;
            try
            {
                batch = await BoundedAsync(token => _client.ProductFamilies.ListProductsForProductFamily(
                    new ListProductsForProductFamilyRequest
                    {
                        ProductFamilyId = "handle:" + ProductFamilyHandle,
                        Page = page,
                        PerPage = PlanPageSize,
                    },
                    cancellationToken: token), cancellationToken);
            }
            catch (ApiException<ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetString(out _))
                {
                    throw CatalogMissing(ex);
                }
                // TryGetRawError (any other status): translated by status.
                throw Translate(ex, "list plans");
            }
            catch (ResponseDeserializationException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                // The 404 body is declared as a string; anything else still means the family does not exist.
                throw CatalogMissing(ex);
            }
            catch (Exception ex) when (IsProviderFailure(ex, cancellationToken))
            {
                throw Translate(ex, "list plans");
            }

            foreach (var product in batch.Select(r => r?.Product))
            {
                if (product?.Handle is null || product.ArchivedAt is not null) continue;
                plans.Add(MapPlan(product));
            }

            if (batch.Count < PlanPageSize) break;
            if (page == MaxPlanPages)
            {
                truncated = true;
                _logger.LogWarning("Plan listing for product family {ProductFamily} stopped at the {MaxPages}-page cap; the result is partial",
                    ProductFamilyHandle, MaxPlanPages);
            }
        }

        var catalog = new PlanCatalog(plans, truncated);
        _cache.Set(cacheKey, catalog, PlanCacheTtl);
        return catalog;
    }

    public async Task<BillingCustomerAccount?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await BoundedAsync(token => _client.Customers.ReadCustomerByReference(
                new ReadCustomerByReferenceRequest { Reference = reference },
                cancellationToken: token), cancellationToken);
            return MapCustomer(response?.Customer) ?? throw Unreadable("find customer");
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex) when (IsProviderFailure(ex, cancellationToken))
        {
            throw Translate(ex, "find customer");
        }
    }

    public async Task<CustomerCreateResult> CreateCustomerAsync(NewBillingCustomer customer, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await BoundedAsync(token => _client.Customers.CreateCustomer(
                new CreateCustomerOperationRequest
                {
                    Body = new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer
                        {
                            FirstName = customer.FirstName,
                            LastName = customer.LastName,
                            Email = customer.Email,
                            Reference = customer.Reference,
                        },
                    },
                },
                cancellationToken: token), cancellationToken);

            var created = MapCustomer(response?.Customer);
            return created is not null
                ? CustomerCreateResult.Succeeded(created)
                : await SettleCustomerAsync(customer.Reference, "the response carried no customer id");
        }
        catch (ApiException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var body))
            {
                // A 422 also covers "reference already taken" — by an earlier attempt of ours, whose customer is ours.
                var mine = await FindCustomerQuietlyAsync(customer.Reference);
                if (mine is not null) return CustomerCreateResult.Succeeded(mine);

                var errors = CustomerErrors(body);
                _logger.LogWarning("Maxio rejected customer {CustomerReference} ({Status}): {Errors}",
                    customer.Reference, (int)ex.StatusCode, string.Join("; ", errors));
                return CustomerCreateResult.Rejected(errors);
            }
            if (ex.Error.TryGetRawError(out var raw) && (int)raw.StatusCode >= 500)
            {
                return await SettleCustomerAsync(customer.Reference, $"HTTP {(int)raw.StatusCode}");
            }
            throw Translate(ex, "create customer");
        }
        catch (ResponseDeserializationException ex) when ((int)ex.StatusCode < 300 || (int)ex.StatusCode >= 500)
        {
            return await SettleCustomerAsync(customer.Reference, $"unreadable HTTP {(int)ex.StatusCode} response");
        }
        catch (SdkConnectionException ex)
        {
            return await SettleCustomerAsync(customer.Reference, ex is SdkTimeoutException ? "timeout" : "connection failure");
        }
        catch (OperationCanceledException)
        {
            return await SettleCustomerAsync(customer.Reference, cancellationToken.IsCancellationRequested ? "caller cancelled" : "call budget exceeded");
        }
        catch (Exception ex) when (IsProviderFailure(ex, cancellationToken))
        {
            throw Translate(ex, "create customer");
        }
    }

    public async Task<BillingSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await BoundedAsync(token => _client.Subscriptions.FindSubscription(
                new FindSubscriptionRequest { Reference = reference },
                cancellationToken: token), cancellationToken);
            return MapSubscription(response?.Subscription) ?? throw Unreadable("find subscription");
        }
        catch (ApiException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                return null;
            }
            // TryGetRawError (any other status): translated by status.
            throw Translate(ex, "find subscription");
        }
        catch (Exception ex) when (IsProviderFailure(ex, cancellationToken))
        {
            throw Translate(ex, "find subscription");
        }
    }

    public async Task<BillingSubscription?> ReadSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await BoundedAsync(token => _client.Subscriptions.ReadSubscription(
                new ReadSubscriptionRequest { SubscriptionId = subscriptionId },
                cancellationToken: token), cancellationToken);
            return MapSubscription(response?.Subscription) ?? throw Unreadable("read subscription");
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex) when (IsProviderFailure(ex, cancellationToken))
        {
            throw Translate(ex, "read subscription");
        }
    }

    public async Task<SubscriptionCreateResult> CreateSubscriptionAsync(NewBillingSubscription subscription, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await BoundedAsync(token => _client.Subscriptions.CreateSubscription(
                new CreateSubscriptionOperationRequest
                {
                    Body = new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            ProductHandle = subscription.PlanHandle,
                            CustomerId = subscription.CustomerId,
                            Reference = subscription.Reference,
                        },
                    },
                },
                cancellationToken: token), cancellationToken);

            var created = MapSubscription(response?.Subscription);
            return created is not null
                ? SubscriptionCreateResult.Succeeded(created)
                : await SettleSubscriptionAsync(subscription.Reference, "the response carried no subscription id");
        }
        catch (ApiException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var body))
            {
                // If the reference is already ours in Maxio, that subscription is the outcome of this request.
                var mine = await FindSubscriptionQuietlyAsync(subscription.Reference);
                if (mine is not null) return SubscriptionCreateResult.Succeeded(mine);

                var errors = body.Errors?.Where(e => !string.IsNullOrWhiteSpace(e)).ToList() ?? [];
                _logger.LogWarning("Maxio rejected subscription {SubscriptionReference} ({Status}): {Errors}",
                    subscription.Reference, (int)ex.StatusCode, string.Join("; ", errors));
                return SubscriptionCreateResult.Rejected(errors);
            }
            if (ex.Error.TryGetRawError(out var raw) && (int)raw.StatusCode >= 500)
            {
                return await SettleSubscriptionAsync(subscription.Reference, $"HTTP {(int)raw.StatusCode}");
            }
            throw Translate(ex, "create subscription");
        }
        catch (ResponseDeserializationException ex) when ((int)ex.StatusCode < 300 || (int)ex.StatusCode >= 500)
        {
            return await SettleSubscriptionAsync(subscription.Reference, $"unreadable HTTP {(int)ex.StatusCode} response");
        }
        catch (SdkConnectionException ex)
        {
            return await SettleSubscriptionAsync(subscription.Reference, ex is SdkTimeoutException ? "timeout" : "connection failure");
        }
        catch (OperationCanceledException)
        {
            return await SettleSubscriptionAsync(subscription.Reference, cancellationToken.IsCancellationRequested ? "caller cancelled" : "call budget exceeded");
        }
        catch (Exception ex) when (IsProviderFailure(ex, cancellationToken))
        {
            throw Translate(ex, "create subscription");
        }
    }

    public async Task<IReadOnlyList<BillingSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await BoundedAsync(token => _client.Customers.ListCustomerSubscriptions(
                new ListCustomerSubscriptionsRequest { CustomerId = customerId },
                cancellationToken: token), cancellationToken);
            return response
                .Select(r => MapSubscription(r?.Subscription))
                .Where(s => s is not null)
                .Select(s => s!)
                .ToList();
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return [];
        }
        catch (Exception ex) when (IsProviderFailure(ex, cancellationToken))
        {
            throw Translate(ex, "list customer subscriptions");
        }
    }

    // ----- unknown-outcome settlement -----

    private async Task<CustomerCreateResult> SettleCustomerAsync(string reference, string reason)
    {
        _logger.LogWarning("Customer create for {CustomerReference} has an unknown outcome ({Reason}); settling by reference", reference, reason);
        var found = await FindCustomerQuietlyAsync(reference);
        return found is not null ? CustomerCreateResult.Succeeded(found) : CustomerCreateResult.Unknown();
    }

    private async Task<SubscriptionCreateResult> SettleSubscriptionAsync(string reference, string reason)
    {
        _logger.LogWarning("Subscription create for {SubscriptionReference} has an unknown outcome ({Reason}); settling by reference", reference, reason);
        var found = await FindSubscriptionQuietlyAsync(reference);
        return found is not null ? SubscriptionCreateResult.Succeeded(found) : SubscriptionCreateResult.Unknown();
    }

    /// <summary>Re-read with its own budget (the caller's may be spent). Null when not found or not readable.</summary>
    private async Task<BillingCustomerAccount?> FindCustomerQuietlyAsync(string reference)
    {
        try
        {
            using var settle = new CancellationTokenSource(SettleBudget);
            return await FindCustomerByReferenceAsync(reference, settle.Token);
        }
        catch (Exception ex) when (ex is BillingException or OperationCanceledException)
        {
            _logger.LogWarning("Could not look up customer {CustomerReference}: {Reason}", reference, ex.Message);
            return null;
        }
    }

    private async Task<BillingSubscription?> FindSubscriptionQuietlyAsync(string reference)
    {
        try
        {
            using var settle = new CancellationTokenSource(SettleBudget);
            return await FindSubscriptionByReferenceAsync(reference, settle.Token);
        }
        catch (Exception ex) when (ex is BillingException or OperationCanceledException)
        {
            _logger.LogWarning("Could not look up subscription {SubscriptionReference}: {Reason}", reference, ex.Message);
            return null;
        }
    }

    // ----- the call boundary -----

    /// <summary>The one place the whole-call budget is applied, linked to the caller's own cancellation.</summary>
    private static async Task<T> BoundedAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(CallBudget);
        return await call(budget.Token);
    }

    /// <summary>Everything the SDK raises, plus our own budget expiring — but never the caller's own cancellation.</summary>
    private static bool IsProviderFailure(Exception ex, CancellationToken callerToken) =>
        ex is SdkException || (ex is OperationCanceledException && !callerToken.IsCancellationRequested);

    private BillingException Translate(Exception ex, string operation)
    {
        switch (ex)
        {
            case ResponseDeserializationException rde:
                _logger.LogError(rde, "Maxio {Operation} returned an unreadable HTTP {Status} body ({Method} {Path})",
                    operation, (int)rde.StatusCode, rde.Method, rde.RequestUri.AbsolutePath);
                return new BillingException(BillingErrorKind.ProviderError,
                    "The billing system returned a response that could not be processed.", innerException: ex);

            case ApiException api when api.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                // Our credentials, not the caller's: never pass a 401/403 through.
                _logger.LogError("Maxio {Operation} refused our credentials: HTTP {Status} ({Method} {Path}); check Maxio:ApiKey / Maxio:Subdomain",
                    operation, (int)api.StatusCode, api.Method, api.RequestUri.AbsolutePath);
                return new BillingException(BillingErrorKind.ProviderError, "Billing is temporarily unavailable.", innerException: ex);

            case ApiException api when api.StatusCode == (HttpStatusCode)429 || (int)api.StatusCode >= 500:
                _logger.LogError("Maxio {Operation} failed: HTTP {Status} ({Method} {Path})",
                    operation, (int)api.StatusCode, api.Method, api.RequestUri.AbsolutePath);
                return new BillingException(BillingErrorKind.ProviderUnavailable,
                    "The billing system is temporarily unavailable. Try again shortly.", innerException: ex);

            case ApiException api:
                _logger.LogWarning("Maxio {Operation} returned HTTP {Status} ({Method} {Path})",
                    operation, (int)api.StatusCode, api.Method, api.RequestUri.AbsolutePath);
                return new BillingException(BillingErrorKind.ProviderError, "The billing system could not process the request.", innerException: ex);

            case SdkTimeoutException timeout:
                _logger.LogError("Maxio {Operation} did not answer within {Timeout} ({Method} {Path})",
                    operation, timeout.Timeout, timeout.Method, timeout.RequestUri.AbsolutePath);
                return new BillingException(BillingErrorKind.ProviderUnavailable,
                    "The billing system did not answer in time. Try again shortly.", innerException: ex);

            case SdkConnectionException connection:
                _logger.LogError(connection.InnerException, "Maxio {Operation} could not be reached ({Method} {Path})",
                    operation, connection.Method, connection.RequestUri.AbsolutePath);
                return new BillingException(BillingErrorKind.ProviderUnavailable,
                    "The billing system could not be reached. Try again shortly.", innerException: ex);

            case AuthSchemeException auth:
                _logger.LogError(auth.InnerException, "Maxio {Operation}: credentials could not be applied", operation);
                return new BillingException(BillingErrorKind.ProviderError, "Billing is temporarily unavailable.", innerException: ex);

            case OperationCanceledException:
                _logger.LogError("Maxio {Operation} exceeded its {Budget} budget", operation, CallBudget);
                return new BillingException(BillingErrorKind.ProviderUnavailable,
                    "The billing system did not answer in time. Try again shortly.", innerException: ex);

            default:
                _logger.LogError(ex, "Maxio {Operation} failed unexpectedly", operation);
                return new BillingException(BillingErrorKind.ProviderError, "Billing is temporarily unavailable.", innerException: ex);
        }
    }

    private BillingException CatalogMissing(Exception ex)
    {
        _logger.LogError("Maxio product family {ProductFamily} was not found; check Maxio:ProductFamilyHandle", ProductFamilyHandle);
        return new BillingException(BillingErrorKind.ProviderError, "The subscription catalog is not available.", innerException: ex);
    }

    private BillingException Unreadable(string operation)
    {
        _logger.LogError("Maxio {Operation} returned a success without the expected record", operation);
        return new BillingException(BillingErrorKind.ProviderError, "The billing system returned a response that could not be processed.");
    }

    // ----- mapping -----

    private static IReadOnlyList<string> CustomerErrors(CustomerErrorResponse1 body)
    {
        if (body.Errors is { } errors)
        {
            if (errors.TryGetListOfString(out var list))
            {
                return list.Where(e => !string.IsNullOrWhiteSpace(e)).ToList();
            }
            if (errors.TryGetCustomerError(out var customerError) && !string.IsNullOrWhiteSpace(customerError.Customer))
            {
                return [customerError.Customer];
            }
        }
        return [];
    }

    private static SubscriptionPlan MapPlan(Product product) => new(
        product.Id,
        product.Handle!,
        string.IsNullOrWhiteSpace(product.Name) ? product.Handle! : product.Name,
        product.Description,
        product.PriceInCents ?? 0,
        product.Interval,
        product.IntervalUnit?.Value,
        product.TrialPriceInCents,
        product.TrialInterval,
        product.TrialIntervalUnit?.Value,
        product.InitialChargeInCents,
        product.RequireCreditCard ?? false);

    private static BillingCustomerAccount? MapCustomer(Customer? customer) =>
        customer?.Id is int id ? new BillingCustomerAccount(id, customer.Reference) : null;

    private static BillingSubscription? MapSubscription(Subscription? subscription)
    {
        if (subscription?.Id is not int id) return null;
        var product = subscription.Product;
        return new BillingSubscription(
            id,
            subscription.Reference,
            subscription.State?.Value ?? "unknown",
            product?.Handle,
            product?.Name,
            product?.ProductFamily?.Handle,
            subscription.ProductPriceInCents ?? product?.PriceInCents,
            subscription.Currency,
            product?.Interval,
            product?.IntervalUnit?.Value,
            subscription.CurrentPeriodStartedAt,
            subscription.CurrentPeriodEndsAt,
            subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
            subscription.ActivatedAt,
            subscription.CreatedAt,
            subscription.ExpiresAt,
            subscription.CancelAtEndOfPeriod ?? false);
    }
}
