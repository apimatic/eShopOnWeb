using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
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
using Microsoft.eShopWeb.PublicApi.Maxio.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// The single boundary over the Maxio Advanced Billing SDK. Every SDK call is wrapped here; every SDK
/// failure — API error, transport failure, or unreadable/drifted body — is translated to a
/// <see cref="MaxioBillingException"/> carrying a caller-safe message and an outward HTTP status. Raw
/// provider detail is logged, never surfaced to callers.
/// </summary>
public class MaxioBillingService : IMaxioBillingService
{
    // Serializes a single shopper's subscribe attempts within this process so a double-click cannot
    // create two customers or two subscriptions. Keyed by the stable shopper reference. Static so it is
    // shared across the scoped service instances of a process (single-instance deployment).
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscribeLocks = new();

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioBillingService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    // ------------------------------------------------------------------ public API

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken ct)
    {
        EnsureConfigured();

        var products = await ListProductsAsync(ct);
        var plans = new List<SubscriptionPlanDto>(products.Count);
        foreach (var productResponse in products)
        {
            var product = productResponse.Product;
            if (product is null)
            {
                continue;
            }
            plans.Add(MapPlan(product));
        }
        return plans;
    }

    public async Task<SubscribeResult> SubscribeAsync(ShopperIdentity shopper, string productHandle, CancellationToken ct)
    {
        EnsureConfigured();

        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new MaxioBillingException(
                "A plan handle is required. Choose one from GET /api/subscription-plans.",
                HttpStatusCode.BadRequest);
        }

        var gate = SubscribeLocks.GetOrAdd(shopper.Reference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var customer = await EnsureCustomerAsync(shopper, ct);
            if (customer.Id is not int customerId)
            {
                throw new MaxioBillingException(
                    "The billing provider returned a customer without an id.", HttpStatusCode.BadGateway);
            }

            // Idempotency guard: reuse an existing active subscription to the same plan instead of duplicating.
            var existing = await FindActiveSubscriptionSdkAsync(customerId, productHandle, ct);
            if (existing is not null)
            {
                return new SubscribeResult(MapSubscription(existing), AlreadySubscribed: true);
            }

            var subscription = await CreateSubscriptionInternalAsync(customerId, productHandle, ct);
            return new SubscribeResult(MapSubscription(subscription), AlreadySubscribed: false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(ShopperIdentity shopper, CancellationToken ct)
    {
        EnsureConfigured();

        var customer = await ReadCustomerByReferenceAsync(shopper.Reference, ct);
        if (customer?.Id is not int customerId)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var subscriptions = await ListCustomerSubscriptionsAsync(customerId, ct);
        var result = new List<SubscriptionDto>(subscriptions.Count);
        foreach (var subscriptionResponse in subscriptions)
        {
            if (subscriptionResponse.Subscription is { } subscription)
            {
                result.Add(MapSubscription(subscription));
            }
        }
        return result;
    }

    // ------------------------------------------------------------------ SDK operation wrappers

    private async Task<IReadOnlyList<ProductResponse>> ListProductsAsync(CancellationToken ct)
    {
        var familyId = $"handle:{_settings.ProductFamilyHandle}";
        try
        {
            return await _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: familyId,
                dateField: null,
                filter: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                includeArchived: false,
                include: null,
                page: 1,
                perPage: 200,
                ct: ct);
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out var notFound))
            {
                _logger.LogWarning("Maxio ListProductsForProductFamily 404 for {Family}: {Detail}", familyId, notFound);
                throw new MaxioBillingException(
                    $"Product family '{_settings.ProductFamilyHandle}' was not found.", HttpStatusCode.NotFound, ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw TranslateRawError(raw, "list subscription plans", ex);
            }
            throw new MaxioBillingException("Could not list subscription plans.", HttpStatusCode.BadGateway, ex);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unreachable(ex); }
        catch (JsonException ex) { throw Unreadable(ex); }
    }

    private async Task<Customer?> ReadCustomerByReferenceAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference, ct);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            // 404 is the "customer does not exist yet" signal — the create path. NOT an error.
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRawError(ex.Error, "look up the customer", ex);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unreachable(ex); }
        // A drifted 2xx body is an unknown outcome — NOT a "not found". Never turn a parse failure into an absence.
        catch (JsonException ex) { throw Unreadable(ex); }
    }

    private async Task<Customer> EnsureCustomerAsync(ShopperIdentity shopper, CancellationToken ct)
    {
        var existing = await ReadCustomerByReferenceAsync(shopper.Reference, ct);
        if (existing is not null)
        {
            return existing;
        }

        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = shopper.FirstName,
                LastName = shopper.LastName,
                Email = shopper.Email,
                Reference = shopper.Reference
            }
        };

        try
        {
            var response = await _client.Customers.CreateCustomer(body, ct);
            return response.Customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // A 422 here is commonly a duplicate-reference race (a concurrent create won). Reconcile by
            // re-reading; if the customer now exists, the race resolved and we adopt it.
            var raced = await ReadCustomerByReferenceAsync(shopper.Reference, ct);
            if (raced is not null)
            {
                return raced;
            }
            throw TranslateCreateCustomer(ex);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // The write may have reached Maxio before the connection dropped. Reconcile before failing.
            var raced = await ReadCustomerByReferenceAsync(shopper.Reference, ct);
            if (raced is not null)
            {
                return raced;
            }
            throw Unreachable(ex);
        }
        catch (JsonException ex)
        {
            // Either a drifted 2xx (the create may have succeeded) or a 422 whose body did not match the
            // generated error model (its status was lost with the SdkException). Reconcile first; if the
            // customer is not there, treat it as a deterministic rejection (422), not an outage to retry.
            var raced = await ReadCustomerByReferenceAsync(shopper.Reference, ct);
            if (raced is not null)
            {
                return raced;
            }
            _logger.LogWarning(ex, "Maxio CreateCustomer response could not be parsed.");
            throw new MaxioBillingException(
                "The billing provider rejected the customer details.", HttpStatusCode.UnprocessableEntity, ex);
        }
    }

    private async Task<Subscription> CreateSubscriptionInternalAsync(int customerId, string productHandle, CancellationToken ct)
    {
        var body = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = productHandle,
                CustomerId = customerId,
                // Payment-not-required plans: enroll on a remittance (invoice) basis so no card capture /
                // payment-profile / 3-DS is needed. The default (automatic) collection would attempt an
                // immediate charge and fail with "no payment method on file".
                PaymentCollectionMethod = CollectionMethod.Remittance
            }
        };

        try
        {
            var response = await _client.Subscriptions.CreateSubscription(body, ct);
            if (response.Subscription is not { } subscription)
            {
                throw new MaxioBillingException(
                    "The billing provider returned an empty subscription.", HttpStatusCode.BadGateway);
            }
            return subscription;
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                var detail = errorList.Errors is { Count: > 0 } messages
                    ? string.Join("; ", messages)
                    : "validation failed";
                _logger.LogWarning("Maxio CreateSubscription 422: {Detail}", detail);
                throw new MaxioBillingException(
                    $"The subscription could not be created: {detail}", HttpStatusCode.UnprocessableEntity, ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw TranslateRawError(raw, "create the subscription", ex);
            }
            throw new MaxioBillingException("The subscription could not be created.", HttpStatusCode.BadGateway, ex);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // A transport failure may have created the subscription. Reconcile: if an active subscription to
            // this plan now exists, return it rather than reporting a failure the caller would retry.
            var reconciled = await FindActiveSubscriptionSdkAsync(customerId, productHandle, ct);
            if (reconciled is not null)
            {
                return reconciled;
            }
            throw Unreachable(ex);
        }
        catch (JsonException ex) { throw Unreadable(ex); }
    }

    private async Task<Subscription?> FindActiveSubscriptionSdkAsync(int customerId, string productHandle, CancellationToken ct)
    {
        var subscriptions = await ListCustomerSubscriptionsAsync(customerId, ct);
        foreach (var subscriptionResponse in subscriptions)
        {
            var subscription = subscriptionResponse.Subscription;
            if (subscription is null)
            {
                continue;
            }
            if (string.Equals(subscription.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase)
                && IsActiveLike(subscription.State))
            {
                return subscription;
            }
        }
        return null;
    }

    private async Task<IReadOnlyList<SubscriptionResponse>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct)
    {
        try
        {
            return await _client.Customers.ListCustomerSubscriptions(customerId, ct);
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRawError(ex.Error, "list the customer's subscriptions", ex);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unreachable(ex); }
        catch (JsonException ex) { throw Unreadable(ex); }
    }

    // ------------------------------------------------------------------ translation & mapping helpers

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey)
            || string.IsNullOrWhiteSpace(_settings.Subdomain)
            || string.IsNullOrWhiteSpace(_settings.ProductFamilyHandle))
        {
            throw new MaxioBillingException(
                "Maxio billing is not configured. Provide Maxio:ApiKey, Maxio:Subdomain and Maxio:ProductFamilyHandle.",
                HttpStatusCode.InternalServerError);
        }
    }

    private MaxioBillingException TranslateCreateCustomer(SdkException<CreateCustomerError> ex)
    {
        if (ex.Error.TryGetCustomerErrorResponse1(out var customerError))
        {
            _logger.LogWarning("Maxio CreateCustomer 422: {Detail}", DescribeCustomerErrors(customerError));
            return new MaxioBillingException(
                "The billing provider rejected the customer details.", HttpStatusCode.UnprocessableEntity, ex);
        }
        if (ex.Error.TryGetRawError(out var raw))
        {
            return TranslateRawError(raw, "create the customer", ex);
        }
        return new MaxioBillingException("The customer could not be created.", HttpStatusCode.BadGateway, ex);
    }

    private static string DescribeCustomerErrors(CustomerErrorResponse1 error)
    {
        var parts = new List<string>();
        if (error.Errors?.PerPage is { Count: > 0 } perPage)
        {
            parts.AddRange(perPage);
        }
        if (error.Errors?.PricePoint is { Count: > 0 } pricePoint)
        {
            parts.AddRange(pricePoint);
        }
        return parts.Count > 0 ? string.Join("; ", parts) : "no detail available";
    }

    private MaxioBillingException TranslateRawError(RawError raw, string action, Exception inner)
    {
        var status = raw.StatusCode;
        _logger.LogWarning("Maxio error while trying to {Action}: HTTP {Status} {Body}",
            action, (int)status, SafeReadBody(raw));
        return MapStatus(status, $"The billing provider could not {action}.", inner);
    }

    private static string SafeReadBody(RawError raw)
    {
        try
        {
            return raw.ReadAsString();
        }
        catch
        {
            return "<unreadable>";
        }
    }

    private static MaxioBillingException MapStatus(HttpStatusCode status, string message, Exception? inner)
    {
        // A provider 4xx the caller can act on stays a 4xx; anything else surfaces as 502 (provider problem).
        var code = (int)status;
        var outward = code is >= 400 and < 500 ? status : HttpStatusCode.BadGateway;
        return new MaxioBillingException(message, outward, inner);
    }

    private MaxioBillingException Unreachable(Exception ex)
    {
        _logger.LogError(ex, "Maxio billing provider is unreachable.");
        return new MaxioBillingException(
            "The billing provider is currently unreachable. Please try again.", HttpStatusCode.BadGateway, ex);
    }

    private MaxioBillingException Unreadable(Exception ex)
    {
        _logger.LogError(ex, "Maxio billing provider returned an unreadable response.");
        return new MaxioBillingException(
            "The billing provider returned a response that could not be processed.", HttpStatusCode.BadGateway, ex);
    }

    private static SubscriptionPlanDto MapPlan(Product product) => new()
    {
        Handle = product.Handle ?? string.Empty,
        ProductId = product.Id,
        Name = product.Name ?? string.Empty,
        Description = product.Description,
        PriceInCents = product.PriceInCents ?? 0,
        FormattedPrice = FormatPrice(product.PriceInCents),
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit?.Value
    };

    private static SubscriptionDto MapSubscription(Subscription subscription) => new()
    {
        Id = subscription.Id,
        ProductHandle = subscription.Product?.Handle,
        ProductName = subscription.Product?.Name,
        State = subscription.State?.Value,
        PriceInCents = subscription.ProductPriceInCents,
        FormattedPrice = FormatPrice(subscription.ProductPriceInCents),
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingAt = subscription.NextAssessmentAt
    };

    private static bool IsActiveLike(SubscriptionState? state) =>
        state == SubscriptionState.Active
        || state == SubscriptionState.Trialing
        || state == SubscriptionState.Pending;

    private static string FormatPrice(long? cents) =>
        cents.HasValue ? "$" + (cents.Value / 100m).ToString("0.00", CultureInfo.InvariantCulture) : string.Empty;
}
