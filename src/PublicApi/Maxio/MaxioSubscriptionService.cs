using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
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
using MaxioAdvancedBilling.Models.AnyOf;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Typed wrapper around the Maxio Advanced Billing SDK. This is the single service that
/// talks to Maxio; it owns the error boundary (SDK/transport failures are translated to
/// <see cref="MaxioException"/> subtypes) and makes subscribe idempotent under double-click.
/// </summary>
public sealed class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private const int ProductsPerPage = 20;
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    // Subscription states that mean the subscription is over and the product is free to be
    // subscribed to again. Any other state (active, trialing, past_due, unpaid, on_hold,
    // pending, ...) still occupies the plan for this customer.
    private static readonly HashSet<string> EndedSubscriptionStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled",
        "expired",
        "trial_ended",
        "failed_to_create"
    };

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    // Per-customer gates so a double-click on subscribe cannot create two subscriptions.
    // Keyed by the Maxio customer reference (the application user id). Single-instance only;
    // a multi-instance deployment needs a distributed lock with the same shape.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _subscribeGates = new(StringComparer.Ordinal);

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioOptions> options,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<MaxioPlan>> ListPlansAsync(CancellationToken cancellationToken)
    {
        EnsureConfigured();
        return await WithBudgetAsync(async ct =>
        {
            var family = await ResolveProductFamilyAsync(_options.ProductFamilyHandle!, ct);

            var plans = new List<MaxioPlan>();
            var page = 1;
            while (true)
            {
                var pageResponse = await _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: family.Id.Value.ToString(CultureInfo.InvariantCulture),
                    dateField: null,
                    filter: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    includeArchived: false,
                    include: null,
                    page: page,
                    perPage: ProductsPerPage,
                    ct: ct);

                foreach (var item in pageResponse)
                {
                    if (item.Product is { } product)
                    {
                        plans.Add(await BuildPlanAsync(product, ct));
                    }
                }

                if (pageResponse.Count < ProductsPerPage)
                {
                    break;
                }

                page++;
            }

            return (IReadOnlyList<MaxioPlan>)plans;
        }, cancellationToken);
    }

    public async Task<MaxioSubscribeResult> SubscribeAsync(MaxioCustomerProfile customer, string planHandle, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        if (customer is null)
        {
            throw new ArgumentNullException(nameof(customer));
        }

        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new MaxioApiException(HttpStatusCode.BadRequest, "A plan handle is required to subscribe.", null);
        }

        var gate = _subscribeGates.GetOrAdd(customer.Reference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await WithBudgetAsync(ct => SubscribeCoreAsync(customer, planHandle.Trim(), ct), cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsAsync(MaxioCustomerProfile customer, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        if (customer is null)
        {
            throw new ArgumentNullException(nameof(customer));
        }

        return await WithBudgetAsync(async ct =>
        {
            var maxioCustomer = await TryReadCustomerByReferenceAsync(customer.Reference, ct);
            if (maxioCustomer is null || maxioCustomer.Id is not { } customerId)
            {
                return (IReadOnlyList<MaxioSubscription>)Array.Empty<MaxioSubscription>();
            }

            return await ReadSubscriptionsAsync(customerId, ct);
        }, cancellationToken);
    }

    private async Task<MaxioSubscribeResult> SubscribeCoreAsync(MaxioCustomerProfile customer, string planHandle, CancellationToken ct)
    {
        var maxioCustomer = await EnsureCustomerAsync(customer, ct);
        if (maxioCustomer.Id is not { } customerId)
        {
            throw new MaxioUnavailableException(
                HttpStatusCode.BadGateway,
                "Maxio created the customer but returned no customer id.",
                innerException: null);
        }

        var existing = await FindSubscriptionForPlanAsync(customerId, planHandle, ct);
        if (existing is not null)
        {
            _logger.LogInformation("Customer {CustomerReference} already holds plan {PlanHandle}; returning the existing subscription {SubscriptionId}.",
                customer.Reference, planHandle, existing.Id);
            return new MaxioSubscribeResult(existing, Created: false);
        }

        // No-card subscriptions on a no-trial product would be rejected by Maxio when it tries
        // to collect the initial charge immediately. Defer the first billing attempt to the
        // plan's next period so the subscription is created without a payment method on file.
        var nextBillingAt = await ResolvePlanNextBillingAtAsync(planHandle, ct);

        try
        {
            var response = await _client.Subscriptions.CreateSubscription(new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = planHandle,
                    CustomerReference = customer.Reference,
                    NextBillingAt = nextBillingAt
                }
            }, ct: ct);

            if (response.Subscription is not { } created)
            {
                throw new MaxioUnavailableException(
                    HttpStatusCode.BadGateway,
                    "Maxio accepted the subscription but returned no subscription payload.",
                    innerException: null);
            }

            _logger.LogInformation("Customer {CustomerReference} subscribed to plan {PlanHandle}; Maxio subscription {SubscriptionId} created (state {State}).",
                customer.Reference, planHandle, created.Id, created.State?.Value);
            return new MaxioSubscribeResult(ToSubscription(created), Created: true);
        }
        catch (Exception ex) when (ex is SdkException<CreateSubscriptionError> or SdkException<RawError> or HttpRequestException or JsonException)
        {
            // A failure on create may be Maxio rejecting the duplicate (another request won
            // the race) or a transport failure after the request was delivered. Re-read the
            // customer's subscriptions and treat a matching live subscription as success.
            var after = await TryFindSubscriptionForPlanAsync(customerId, planHandle, ct);
            if (after is not null)
            {
                _logger.LogInformation("Duplicate subscription create for customer {CustomerReference} on plan {PlanHandle}; returning the existing subscription {SubscriptionId}.",
                    customer.Reference, planHandle, after.Id);
                return new MaxioSubscribeResult(after, Created: false);
            }

            throw Translate(ex);
        }
    }

    private async Task<Customer> EnsureCustomerAsync(MaxioCustomerProfile customer, CancellationToken ct)
    {
        var existing = await TryReadCustomerByReferenceAsync(customer.Reference, ct);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            var response = await _client.Customers.CreateCustomer(new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = customer.FirstName,
                    LastName = customer.LastName,
                    Email = customer.Email,
                    Reference = customer.Reference
                }
            }, ct: ct);

            if (response.Customer is not { } created)
            {
                throw new MaxioUnavailableException(
                    HttpStatusCode.BadGateway,
                    "Maxio accepted the customer but returned no customer payload.",
                    innerException: null);
            }

            _logger.LogInformation("Created Maxio customer {CustomerId} for eShop user reference {CustomerReference}.",
                created.Id, customer.Reference);
            return created;
        }
        catch (Exception ex) when (ex is SdkException<CreateCustomerError> or SdkException<RawError> or HttpRequestException or JsonException)
        {
            // Maxio enforces reference uniqueness. A create failure here usually means a
            // concurrent request created the customer first (double-click). Re-read by
            // reference and treat "now exists" as success; otherwise surface the error.
            var after = await TryReadCustomerByReferenceAsync(customer.Reference, ct);
            if (after is not null)
            {
                return after;
            }

            throw Translate(ex);
        }
    }

    /// <summary>
    /// Resolves the plan's billing cadence and returns the date the first billing attempt
    /// should be deferred to (one cadence from now). A no-card, no-trial subscription cannot be
    /// created when an initial charge is due immediately, so the create call defers it.
    /// </summary>
    private async Task<DateTimeOffset> ResolvePlanNextBillingAtAsync(string planHandle, CancellationToken ct)
    {
        var productResponse = await _client.Products.ReadProductByHandle(apiHandle: planHandle, ct: ct);
        var product = productResponse.Product;
        return ComputeNextBillingDate(product?.Interval, product?.IntervalUnit?.Value, DateTimeOffset.UtcNow);
    }

    private static DateTimeOffset ComputeNextBillingDate(int? intervalCount, string? intervalUnit, DateTimeOffset now)
    {
        var count = intervalCount is > 0 ? intervalCount.Value : 1;
        switch (intervalUnit)
        {
            case "day":
                return now.AddDays(count);
            case "week":
                return now.AddDays(7 * count);
            case "month":
                return now.AddMonths(count);
            case "year":
                return now.AddYears(count);
            default:
                return now.AddMonths(1);
        }
    }

    private async Task<MaxioPlan> BuildPlanAsync(Product product, CancellationToken ct)
    {
        var priceInCents = product.PriceInCents;
        var interval = product.Interval;
        var intervalUnit = product.IntervalUnit;

        if (priceInCents is null || interval is null || intervalUnit is null)
        {
            var pricePoint = await ResolveDefaultPricePointAsync(product, ct);
            if (pricePoint is not null)
            {
                priceInCents ??= pricePoint.PriceInCents;
                interval ??= pricePoint.Interval;
                intervalUnit ??= pricePoint.IntervalUnit;
            }
        }

        return new MaxioPlan(
            product.Handle ?? string.Empty,
            product.Name ?? string.Empty,
            priceInCents,
            interval,
            intervalUnit?.Value);
    }

    private async Task<ProductPricePoint?> ResolveDefaultPricePointAsync(Product product, CancellationToken ct)
    {
        if (product.Id is not { } productId)
        {
            return null;
        }

        var response = await _client.ProductPricePoints.ListProductPricePoints(
            productId: ProductIdModel.Int(productId),
            currencyPrices: null,
            filterType: null,
            archived: null,
            page: 1,
            perPage: 10,
            ct: ct);

        return response.PricePoints.FirstOrDefault(p => p.Type == PricePointType.Default)
               ?? response.PricePoints.FirstOrDefault();
    }

    private async Task<ProductFamily> ResolveProductFamilyAsync(string familyHandle, CancellationToken ct)
    {
        var response = await _client.ProductFamilies.ListProductFamilies(
            dateField: null,
            startDate: null,
            endDate: null,
            startDatetime: null,
            endDatetime: null,
            ct: ct);

        var family = response
            .Select(r => r.ProductFamily)
            .FirstOrDefault(pf => pf is not null && string.Equals(pf.Handle, familyHandle, StringComparison.OrdinalIgnoreCase));

        if (family is null)
        {
            throw new MaxioConfigurationException(
                $"The configured Maxio product family '{familyHandle}' was not found. Check Maxio:ProductFamilyHandle.");
        }

        if (family.Id is null)
        {
            throw new MaxioConfigurationException(
                $"The Maxio product family '{familyHandle}' was found but carries no id.");
        }

        return family;
    }

    private async Task<IReadOnlyList<MaxioSubscription>> ReadSubscriptionsAsync(int customerId, CancellationToken ct)
    {
        var response = await _client.Customers.ListCustomerSubscriptions(customerId: customerId, ct: ct);
        var subscriptions = response
            .Select(r => r.Subscription)
            .Where(s => s is not null)
            .Select(ToSubscription)
            .OrderByDescending(s => s.Id)
            .ToList();
        return subscriptions;
    }

    private async Task<MaxioSubscription?> FindSubscriptionForPlanAsync(int customerId, string planHandle, CancellationToken ct)
    {
        return await TryFindSubscriptionForPlanAsync(customerId, planHandle, ct);
    }

    private async Task<MaxioSubscription?> TryFindSubscriptionForPlanAsync(int customerId, string planHandle, CancellationToken ct)
    {
        try
        {
            var subscriptions = await ReadSubscriptionsAsync(customerId, ct);
            return subscriptions.FirstOrDefault(s =>
                string.Equals(s.PlanHandle, planHandle, StringComparison.OrdinalIgnoreCase)
                && !IsEnded(s.State));
        }
        catch (Exception ex)
        {
            // We must never fail the subscribe flow because the reconciliation read failed;
            // translate and rethrow so the original create error is not masked.
            throw Translate(ex);
        }
    }

    private async Task<Customer?> TryReadCustomerByReferenceAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference: reference, ct: ct);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private static bool IsEnded(string? state) =>
        state is not null && EndedSubscriptionStates.Contains(state);

    private static MaxioSubscription ToSubscription(Subscription subscription)
    {
        var product = subscription.Product;
        return new MaxioSubscription(
            Id: subscription.Id ?? 0,
            PlanHandle: product?.Handle,
            PlanName: product?.Name,
            PriceInCents: subscription.ProductPriceInCents,
            Currency: subscription.Currency,
            IntervalCount: product?.Interval,
            IntervalUnit: product?.IntervalUnit?.Value,
            State: subscription.State?.Value,
            CurrentPeriodEndsAt: subscription.CurrentPeriodEndsAt,
            NextAssessmentAt: subscription.NextAssessmentAt);
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new MaxioConfigurationException("Maxio is not configured: Maxio:ApiKey is missing.");
        }

        if (string.IsNullOrWhiteSpace(_options.Subdomain))
        {
            throw new MaxioConfigurationException("Maxio is not configured: Maxio:Subdomain is missing.");
        }

        if (string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw new MaxioConfigurationException("Maxio is not configured: Maxio:ProductFamilyHandle is missing.");
        }
    }

    private async Task<T> WithBudgetAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CallBudget);
        try
        {
            return await call(timeout.Token);
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            throw Translate(ex);
        }
    }

    private Exception Translate(Exception ex)
    {
        switch (ex)
        {
            case MaxioException maxio:
                return maxio;
            case OperationCanceledException:
                return new MaxioUnavailableException(
                    HttpStatusCode.GatewayTimeout,
                    "Maxio did not respond before the timeout.",
                    ex);
            case SdkException<RawError> raw:
                return TranslateRaw(raw.Error, raw);
            case SdkException<CreateCustomerError> createCustomer:
                return TranslateCreateCustomerError(createCustomer.Error, createCustomer);
            case SdkException<CreateSubscriptionError> createSubscription:
                return TranslateCreateSubscriptionError(createSubscription.Error, createSubscription);
            case SdkException<ListProductsForProductFamilyError> listProducts:
                return TranslateTypedError(listProducts.Error, "Maxio rejected the product list request.", listProducts);
            case HttpRequestException:
                return new MaxioUnavailableException(HttpStatusCode.BadGateway, "Maxio could not be reached.", ex);
            case JsonException:
                return new MaxioUnavailableException(
                    HttpStatusCode.BadGateway,
                    "Maxio returned a response that could not be processed.",
                    ex);
            default:
                return ex;
        }
    }

    private static MaxioException TranslateRaw(RawError raw, Exception? innerException)
    {
        var body = ReadRawBody(raw);
        var message = string.IsNullOrWhiteSpace(body)
            ? $"Maxio request failed with HTTP {(int)raw.StatusCode}."
            : body;
        return new MaxioApiException(raw.StatusCode, message, innerException);
    }

    private static string ReadRawBody(RawError raw)
    {
        try
        {
            var body = raw.ReadAsString();
            return string.IsNullOrEmpty(body)
                ? string.Empty
                : body.Length <= 500 ? body : body.Substring(0, 500) + "…";
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static MaxioException TranslateCreateCustomerError(CreateCustomerError error, Exception innerException)
    {
        if (error.TryGetRawError(out var raw))
        {
            return TranslateRaw(raw, innerException);
        }

        return new MaxioApiException(
            HttpStatusCode.UnprocessableEntity,
            "Maxio rejected the customer request.",
            innerException);
    }

    private static MaxioException TranslateCreateSubscriptionError(CreateSubscriptionError error, Exception innerException)
    {
        if (error.TryGetRawError(out var raw))
        {
            return TranslateRaw(raw, innerException);
        }

        if (error.TryGetErrorListResponse1(out var list) && list.Errors is { Count: > 0 })
        {
            return new MaxioApiException(
                HttpStatusCode.UnprocessableEntity,
                string.Join(" ", list.Errors),
                innerException);
        }

        return new MaxioApiException(
            HttpStatusCode.UnprocessableEntity,
            "Maxio rejected the subscription request.",
            innerException);
    }

    private static MaxioException TranslateTypedError(ApiError error, string fallbackMessage, Exception innerException)
    {
        if (error.TryGetRawError(out var raw))
        {
            return TranslateRaw(raw, innerException);
        }

        return new MaxioApiException(HttpStatusCode.UnprocessableEntity, fallbackMessage, innerException);
    }
}
