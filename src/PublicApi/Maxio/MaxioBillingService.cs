using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// The Maxio integration boundary. All SDK calls go through here with a whole-call
/// cancellation budget, find-or-create idempotency keyed on stable references, and
/// translation of every SDK/transport failure into <see cref="MaxioBillingException"/>.
/// </summary>
public class MaxioBillingService
{
    public const string HttpClientName = "Maxio";

    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private const int ProductPageSize = 100;

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly IMemoryCache _cache;
    private readonly ISubscriptionUserContextAccessor _userContext;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioOptions> options,
        IMemoryCache cache,
        ISubscriptionUserContextAccessor userContext,
        ILogger<MaxioBillingService> logger)
    {
        _client = client;
        _options = options.Value;
        _cache = cache;
        _userContext = userContext;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken ct)
    {
        var familyId = await GetProductFamilyIdAsync(ct);

        var plans = new List<SubscriptionPlanDto>();
        var page = 1;
        while (true)
        {
            IReadOnlyList<ProductResponse> products;
            try
            {
                products = await GuardedAsync(c => _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: familyId.ToString(CultureInfo.InvariantCulture),
                    dateField: null,
                    filter: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    includeArchived: null,
                    include: null,
                    page: page,
                    perPage: ProductPageSize,
                    ct: c), ct);
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetString(out var message))
                {
                    throw new MaxioBillingException(HttpStatusCode.BadGateway,
                        $"The billing provider could not list plans: {message}", ex);
                }
                else if (ex.Error.TryGetRawError(out var raw))
                {
                    throw Translate(raw, ex);
                }
                throw new MaxioBillingException(HttpStatusCode.BadGateway,
                    "The billing provider could not list plans.", ex);
            }

            foreach (var envelope in products)
            {
                var product = envelope.Product;
                if (product is null || product.ArchivedAt is not null)
                {
                    continue;
                }

                plans.Add(new SubscriptionPlanDto
                {
                    Name = product.Name ?? string.Empty,
                    Handle = product.Handle ?? string.Empty,
                    Price = ToDollars(product.PriceInCents),
                    Interval = product.Interval ?? 1,
                    IntervalUnit = product.IntervalUnit?.Value ?? string.Empty
                });
            }

            if (products.Count < ProductPageSize)
            {
                break;
            }
            page++;
        }

        return plans;
    }

    public async Task<SubscriptionDto> SubscribeAsync(ClaimsPrincipal principal, string productHandle, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new MaxioBillingException(HttpStatusCode.BadRequest, "A product handle is required.");
        }

        var customer = await _userContext.GetCurrentCustomerAsync(principal);

        var plans = await ListPlansAsync(ct);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, productHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new MaxioBillingException(HttpStatusCode.NotFound, $"Unknown subscription plan '{productHandle}'.");
        }

        var customerId = await FindOrCreateCustomerIdAsync(customer, ct);

        var reference = $"{customer.Reference}:{plan.Handle}";
        var subscription = await FindOrCreateSubscriptionAsync(customerId, plan, reference, ct);

        _logger.LogInformation("User {CustomerReference} subscribed to plan {ProductHandle} (subscription {SubscriptionId})",
            customer.Reference, plan.Handle, subscription.Id);

        return Map(subscription);
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(ClaimsPrincipal principal, CancellationToken ct)
    {
        var customer = await _userContext.GetCurrentCustomerAsync(principal);

        var customerId = await FindCustomerIdOrNullAsync(customer.Reference, ct);
        if (customerId is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        IReadOnlyList<SubscriptionResponse> subscriptions;
        try
        {
            subscriptions = await GuardedAsync(
                c => _client.Customers.ListCustomerSubscriptions(customerId.Value, ct: c), ct);
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex.Error, ex);
        }

        return subscriptions
            .Select(s => s.Subscription)
            .Where(s => s is not null)
            .Select(s => Map(s!))
            .ToList();
    }

    private async Task<int> GetProductFamilyIdAsync(CancellationToken ct)
    {
        var cacheKey = $"{HttpClientName}:product-family-id:{_options.ProductFamilyHandle}";
        if (_cache.TryGetValue(cacheKey, out int cachedId))
        {
            return cachedId;
        }

        IReadOnlyList<ProductFamilyResponse> families;
        try
        {
            families = await GuardedAsync(c => _client.ProductFamilies.ListProductFamilies(
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                ct: c), ct);
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex.Error, ex);
        }

        var match = families
            .Select(f => f.ProductFamily)
            .FirstOrDefault(f => string.Equals(f?.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase));
        if (match?.Id is not int id)
        {
            throw new MaxioBillingException(HttpStatusCode.BadGateway,
                $"The configured Maxio product family '{_options.ProductFamilyHandle}' was not found.");
        }

        _cache.Set(cacheKey, id);
        return id;
    }

    private async Task<int> FindOrCreateCustomerIdAsync(BillingCustomerContext customer, CancellationToken ct)
    {
        var existingId = await FindCustomerIdOrNullAsync(customer.Reference, ct);
        if (existingId is not null)
        {
            return existingId.Value;
        }

        try
        {
            var created = await GuardedAsync(c => _client.Customers.CreateCustomer(
                body: new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = customer.FirstName,
                        LastName = customer.LastName,
                        Email = customer.Email,
                        Reference = customer.Reference
                    }
                }, ct: c), ct);

            if (created.Customer?.Id is int newId)
            {
                return newId;
            }
            throw new MaxioBillingException(HttpStatusCode.BadGateway,
                "The billing provider returned an unexpected customer response.");
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                // 422 — a duplicate-reference race means the customer now exists; re-lookup before failing.
                var racedId = await FindCustomerIdOrNullAsync(customer.Reference, ct);
                if (racedId is not null)
                {
                    return racedId.Value;
                }
                throw new MaxioBillingException(HttpStatusCode.UnprocessableEntity,
                    "The billing provider rejected the customer.", ex);
            }
            else if (ex.Error.TryGetRawError(out var raw))
            {
                throw Translate(raw, ex);
            }
            throw new MaxioBillingException(HttpStatusCode.BadGateway,
                "The billing provider rejected the customer.", ex);
        }
        catch (MaxioBillingException ex) when (ex.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            // Transport failure on a write: the create may still have reached Maxio. Reconcile before failing.
            var reconciledId = await FindCustomerIdOrNullAsync(customer.Reference, ct);
            if (reconciledId is not null)
            {
                return reconciledId.Value;
            }
            throw;
        }
    }

    private async Task<int?> FindCustomerIdOrNullAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await GuardedAsync(
                c => _client.Customers.ReadCustomerByReference(reference: reference, ct: c), ct);
            return response.Customer?.Id;
        }
        catch (SdkException<RawError> ex)
        {
            if (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
            throw Translate(ex.Error, ex);
        }
    }

    private async Task<Subscription> FindOrCreateSubscriptionAsync(int customerId, SubscriptionPlanDto plan, string reference, CancellationToken ct)
    {
        var existing = await FindSubscriptionOrNullAsync(reference, ct);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            var created = await GuardedAsync(c => _client.Subscriptions.CreateSubscription(
                body: new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = plan.Handle,
                        CustomerId = customerId,
                        Reference = reference,
                        // This integration never captures cards; a future next_billing_at defers the
                        // first charge to the renewal date, so signup succeeds without a payment method.
                        NextBillingAt = ComputeFirstBillingDate(plan)
                    }
                }, ct: c), ct);

            if (created.Subscription is not null)
            {
                return created.Subscription;
            }
            throw new MaxioBillingException(HttpStatusCode.BadGateway,
                "The billing provider returned an unexpected subscription response.");
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errors))
            {
                // 422 — re-check in case a concurrent/raced create already enrolled the customer.
                var raced = await FindSubscriptionOrNullAsync(reference, ct);
                if (raced is not null)
                {
                    return raced;
                }
                var detail = errors.Errors is { Count: > 0 } ? string.Join("; ", errors.Errors) : "no details provided";
                throw new MaxioBillingException(HttpStatusCode.UnprocessableEntity,
                    $"The billing provider rejected the subscription: {detail}", ex);
            }
            else if (ex.Error.TryGetRawError(out var raw))
            {
                throw Translate(raw, ex);
            }
            throw new MaxioBillingException(HttpStatusCode.BadGateway,
                "The billing provider rejected the subscription.", ex);
        }
        catch (MaxioBillingException ex) when (ex.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            // Transport failure on a write: the create may still have reached Maxio. Reconcile before failing.
            var reconciled = await FindSubscriptionOrNullAsync(reference, ct);
            if (reconciled is not null)
            {
                return reconciled;
            }
            throw;
        }
    }

    private async Task<Subscription?> FindSubscriptionOrNullAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await GuardedAsync(
                c => _client.Subscriptions.FindSubscription(reference: reference, ct: c), ct);
            return response.Subscription;
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                return null;
            }
            else if (ex.Error.TryGetRawError(out var raw))
            {
                throw Translate(raw, ex);
            }
            throw new MaxioBillingException(HttpStatusCode.BadGateway,
                "The billing provider could not look up the subscription.", ex);
        }
    }

    private async Task<T> GuardedAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        try
        {
            return await call(cts.Token);
        }
        catch (JsonException ex)
        {
            throw new MaxioBillingException(HttpStatusCode.BadGateway,
                "The billing provider returned a response that could not be processed.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new MaxioBillingException(HttpStatusCode.ServiceUnavailable,
                "The billing provider is unreachable.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new MaxioBillingException(HttpStatusCode.ServiceUnavailable,
                "The billing provider timed out.", ex);
        }
    }

    private static DateTimeOffset ComputeFirstBillingDate(SubscriptionPlanDto plan)
    {
        var now = DateTimeOffset.UtcNow;
        return plan.IntervalUnit.Equals("day", StringComparison.OrdinalIgnoreCase)
            ? now.AddDays(plan.Interval)
            : now.AddMonths(plan.Interval);
    }

    private static MaxioBillingException Translate(RawError raw, Exception inner)
    {
        var status = (int)raw.StatusCode is >= 400 and < 500
            ? raw.StatusCode
            : HttpStatusCode.BadGateway;
        return new MaxioBillingException(status,
            $"The billing provider rejected the request (HTTP {(int)raw.StatusCode}).", inner);
    }

    private static SubscriptionDto Map(Subscription subscription) => new()
    {
        Id = subscription.Id ?? 0,
        Reference = subscription.Reference ?? string.Empty,
        ProductName = subscription.Product?.Name ?? string.Empty,
        ProductHandle = subscription.Product?.Handle ?? string.Empty,
        Price = ToDollars(subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents),
        State = subscription.State?.Value ?? string.Empty,
        NextBillingDate = subscription.CurrentPeriodEndsAt,
        CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod ?? false,
        CanceledAt = subscription.CanceledAt
    };

    private static decimal ToDollars(long? priceInCents) => priceInCents is long cents ? cents / 100m : 0m;
}
