using System;
using System.Collections.Concurrent;
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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Maxio Advanced Billing implementation of <see cref="ISubscriptionBillingService"/>.
///
/// Idempotency: both the billing customer and the subscription carry client-supplied references
/// that Maxio enforces unique — the customer reference is derived from the application user id and
/// the subscription reference from the user id + plan handle. A double-click therefore converges on
/// lookup-before-create, and a lost create race (or a resent create after a transport failure)
/// ends in a 422 that is reconciled by re-running the reference lookup.
/// </summary>
public class MaxioSubscriptionService : ISubscriptionBillingService
{
    private const string CustomerReferencePrefix = "eshopweb-user-";
    private const string SubscriptionReferencePrefix = "eshopweb-sub-";
    private const int PlansPageSize = 50;

    /// <summary>Total budget for one logical SDK call (all retry attempts included).</summary>
    private static readonly TimeSpan TotalCallBudget = TimeSpan.FromSeconds(45);

    private static readonly string[] NameSeparators = { ".", "_", "-", "+" };

    private readonly MaxioClientProvider _clientProvider;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    // The product family for a given handle is site configuration that does not change mid-run;
    // its numeric id is stable for the process lifetime, so it is resolved once and cached.
    private readonly ConcurrentDictionary<string, int> _productFamilyIds = new(StringComparer.OrdinalIgnoreCase);

    public MaxioSubscriptionService(MaxioClientProvider clientProvider, ILogger<MaxioSubscriptionService> logger)
    {
        _clientProvider = clientProvider;
        _logger = logger;
    }

    private MaxioAdvancedBillingClient Client => _clientProvider.GetClient();

    private MaxioSettings Settings => _clientProvider.GetSettings();

    public async Task<IReadOnlyList<SubscriptionPlanInfo>> ListPlansAsync(CancellationToken ct)
    {
        var familyId = await ResolveProductFamilyIdAsync(ct);

        var plans = new List<SubscriptionPlanInfo>();
        var page = 1;
        while (true)
        {
            var products = await CallAsync(token => Client.ProductFamilies.ListProductsForProductFamily(
                familyId.ToString(),
                dateField: null,
                filter: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                includeArchived: null,
                include: null,
                page: page,
                perPage: PlansPageSize,
                ct: token), ct);

            plans.AddRange(products
                .Where(p => p.Product is not null)
                .Select(p => MapPlan(p.Product)));

            if (products.Count < PlansPageSize)
            {
                break;
            }
            page++;
        }

        // Deterministic order: cheapest first, then by handle — the head of this list is the
        // default subscribe target when no plan handle is supplied.
        return plans
            .OrderBy(p => p.Price)
            .ThenBy(p => p.Handle, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<SubscriptionInfo> SubscribeAsync(string userId, string userName, string? planHandle, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new SubscriptionBillingException(HttpStatusCode.Unauthorized, "The calling user could not be identified.");
        }

        var customer = await EnsureCustomerAsync(userId, userName, ct);

        if (string.IsNullOrWhiteSpace(planHandle))
        {
            planHandle = (await ListPlansAsync(ct)).FirstOrDefault()?.Handle;
            if (planHandle is null)
            {
                throw new SubscriptionBillingException(HttpStatusCode.InternalServerError,
                    "No subscription plans are configured for this site.");
            }
        }

        var subscriptionReference = $"{SubscriptionReferencePrefix}{userId}-{planHandle}";

        var existing = await TryFindSubscriptionAsync(subscriptionReference, ct);
        if (existing is not null)
        {
            _logger.LogInformation("Subscription {Reference} already exists (id {SubscriptionId}); returning it instead of creating a duplicate.",
                subscriptionReference, existing.Id);
            return MapSubscription(existing, planHandle);
        }

        // Card-free enrollment: the seeded plans accept subscriptions without a payment profile,
        // but Maxio still assesses the first period at signup and rejects the create without one.
        // A future next_billing_at defers the first assessment: no payment is captured at signup,
        // the subscription goes live immediately, and the first charge lands one billing interval out.
        var plan = await TryReadProductAsync(planHandle, ct)
            ?? throw new SubscriptionBillingException(HttpStatusCode.UnprocessableEntity,
                $"No subscription plan with handle '{planHandle}' exists in the billing system.");

        try
        {
            var response = await CallAsync(token => Client.Subscriptions.CreateSubscription(
                new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = planHandle,
                        CustomerId = customer.Id,
                        Reference = subscriptionReference,
                        NextBillingAt = ComputeFirstBillingAt(plan)
                    }
                }, token), ct);

            if (response.Subscription is null)
            {
                throw SubscriptionBillingException.ProviderError(
                    new InvalidOperationException("The create-subscription response carried no subscription."));
            }
            return MapSubscription(response.Subscription, planHandle);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            // A 422 here is either a real validation failure or the lost side of a
            // reference-uniqueness race (including a create re-sent by the transport-retry path).
            // Reconcile against the reference first; only if the subscription still does not exist
            // is the rejection real.
            var raced = await TryFindSubscriptionAsync(subscriptionReference, ct);
            if (raced is not null)
            {
                _logger.LogInformation("Create for subscription {Reference} lost a race or was resent; the subscription exists (id {SubscriptionId}).",
                    subscriptionReference, raced.Id);
                return MapSubscription(raced, planHandle);
            }

            if (ex.Error.TryGetErrorListResponse1(out var validation) && validation.Errors.Count > 0)
            {
                throw new SubscriptionBillingException(HttpStatusCode.UnprocessableEntity,
                    $"The billing system rejected the subscription: {string.Join("; ", validation.Errors)}", ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw MapRaw(raw, ex);
            }
            throw SubscriptionBillingException.ProviderError(ex);
        }
    }

    public async Task<IReadOnlyList<SubscriptionInfo>> ListMySubscriptionsAsync(string userId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new SubscriptionBillingException(HttpStatusCode.Unauthorized, "The calling user could not be identified.");
        }

        var customer = await TryReadCustomerByReferenceAsync(CustomerReference(userId), ct);
        if (customer?.Id is null)
        {
            // No billing customer yet — the user has no subscriptions, which is a normal state.
            return Array.Empty<SubscriptionInfo>();
        }

        var subscriptions = await CallAsync(token => Client.Customers.ListCustomerSubscriptions(customer.Id.Value, token), ct);

        return subscriptions
            .Where(s => s.Subscription is not null)
            .Select(s => MapSubscription(s.Subscription, fallbackPlanHandle: null))
            .ToList();
    }

    private async Task<Customer> EnsureCustomerAsync(string userId, string userName, CancellationToken ct)
    {
        var reference = CustomerReference(userId);

        var existing = await TryReadCustomerByReferenceAsync(reference, ct);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = SplitName(userName);
        try
        {
            var response = await CallAsync(token => Client.Customers.CreateCustomer(
                new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = firstName,
                        LastName = lastName,
                        Email = userName,
                        Reference = reference
                    }
                }, token), ct);

            if (response.Customer is null)
            {
                throw SubscriptionBillingException.ProviderError(
                    new InvalidOperationException("The create-customer response carried no customer."));
            }
            _logger.LogInformation("Created billing customer {CustomerId} for user {UserId}.", response.Customer.Id, userId);
            return response.Customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // The customer-422 body shape in the SDK is a drifted shared model, so read the reason
            // from the raw body; the reconciliation lookup is the reliable outcome check.
            var raced = await TryReadCustomerByReferenceAsync(reference, ct);
            if (raced is not null)
            {
                _logger.LogInformation("Create for customer {Reference} lost a race or was resent; the customer exists (id {CustomerId}).",
                    reference, raced.Id);
                return raced;
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw MapRaw(raw, ex);
            }
            throw SubscriptionBillingException.ProviderError(ex);
        }
    }

    private async Task<Customer?> TryReadCustomerByReferenceAsync(string reference, CancellationToken ct)
    {
        // The 404 check lives inside the delegated call so it sees the original SdkException
        // before CallAsync's translation into SubscriptionBillingException.
        return await CallAsync(async token =>
        {
            try
            {
                var response = await Client.Customers.ReadCustomerByReference(reference, token);
                return response.Customer;
            }
            catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                // 404 = no customer with this reference — the create signal, not an outage.
                return null;
            }
        }, ct);
    }

    private async Task<Product?> TryReadProductAsync(string handle, CancellationToken ct)
    {
        // The 404 check lives inside the delegated call so it sees the original SdkException
        // before CallAsync's translation into SubscriptionBillingException.
        return await CallAsync(async token =>
        {
            try
            {
                var response = await Client.Products.ReadProductByHandle(handle, token);
                return response.Product;
            }
            catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
        }, ct);
    }

    /// <summary>
    /// The first billing date for a card-free signup: one billing interval out, computed from the
    /// plan's own interval and unit. No payment is captured before this date.
    /// </summary>
    private static DateTimeOffset ComputeFirstBillingAt(Product plan)
    {
        var interval = Math.Max(plan.Interval ?? 1, 1);
        var now = DateTimeOffset.UtcNow;
        return string.Equals(plan.IntervalUnit?.Value, "day", StringComparison.OrdinalIgnoreCase)
            ? now.AddDays(interval)
            : now.AddMonths(interval);
    }

    private async Task<Subscription?> TryFindSubscriptionAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await CallAsync(token => Client.Subscriptions.FindSubscription(reference, token), ct);
            return response.Subscription;
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                return null; // 404 — no subscription with this reference.
            }
            if (ex.Error.TryGetRawError(out var raw) && raw.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
            if (ex.Error.TryGetRawError(out var other))
            {
                throw MapRaw(other, ex);
            }
            throw SubscriptionBillingException.ProviderError(ex);
        }
    }

    private async Task<int> ResolveProductFamilyIdAsync(CancellationToken ct)
    {
        var handle = Settings.ProductFamilyHandle;
        if (_productFamilyIds.TryGetValue(handle, out var cached))
        {
            return cached;
        }

        var families = await CallAsync(token => Client.ProductFamilies.ListProductFamilies(
            dateField: null,
            startDate: null,
            endDate: null,
            startDatetime: null,
            endDatetime: null,
            ct: token), ct);

        var match = families.FirstOrDefault(f =>
            string.Equals(f.ProductFamily?.Handle, handle, StringComparison.OrdinalIgnoreCase));

        if (match?.ProductFamily?.Id is null)
        {
            throw new SubscriptionBillingException(HttpStatusCode.InternalServerError,
                $"No product family with handle '{handle}' exists in the billing system. Check the 'Maxio:ProductFamilyHandle' configuration.");
        }

        _productFamilyIds[handle] = match.ProductFamily.Id.Value;
        return match.ProductFamily.Id.Value;
    }

    private static SubscriptionPlanInfo MapPlan(Product product) => new()
    {
        Handle = product.Handle ?? string.Empty,
        ProductId = product.Id,
        Name = product.Name ?? product.Handle ?? string.Empty,
        Price = CentsToDecimal(product.PriceInCents),
        Interval = product.Interval ?? 1,
        IntervalUnit = product.IntervalUnit?.Value ?? "month",
        TrialSupported = (product.TrialInterval ?? 0) > 0,
        RequireCreditCard = product.RequireCreditCard
    };

    // The nested `product` on a subscription response is best-effort (its wire population is not
    // guaranteed), so fall back to the scalar price and to the handle the caller asked for.
    private static SubscriptionInfo MapSubscription(Subscription subscription, string? fallbackPlanHandle) => new()
    {
        SubscriptionId = subscription.Id ?? 0,
        PlanHandle = subscription.Product?.Handle ?? fallbackPlanHandle,
        PlanName = subscription.Product?.Name,
        Price = CentsToDecimal(subscription.Product?.PriceInCents ?? subscription.ProductPriceInCents),
        State = subscription.State?.Value ?? "unknown",
        NextBillingDate = subscription.CurrentPeriodEndsAt,
        Reference = subscription.Reference,
        CustomerId = subscription.Customer?.Id
    };

    private static SubscriptionBillingException MapRaw(RawError raw, Exception inner)
    {
        var status = (int)raw.StatusCode;
        if (raw.StatusCode == HttpStatusCode.RequestTimeout || raw.StatusCode == HttpStatusCode.TooManyRequests || status >= 500)
        {
            return new SubscriptionBillingException(HttpStatusCode.BadGateway,
                "The billing system is temporarily unavailable. The operation may be retried.", inner);
        }
        if (status >= 400)
        {
            return new SubscriptionBillingException(raw.StatusCode,
                $"The billing system rejected the request (HTTP {status}): {SafeReadBody(raw)}", inner);
        }
        return new SubscriptionBillingException(HttpStatusCode.BadGateway,
            $"The billing system returned an unexpected response (HTTP {status}).", inner);
    }

    private static string SafeReadBody(RawError raw)
    {
        try
        {
            var body = raw.ReadAsString() ?? string.Empty;
            return body.Length <= 300 ? body : body[..300] + "…";
        }
        catch
        {
            return string.Empty;
        }
    }

    private static decimal CentsToDecimal(long? cents) => (cents ?? 0) / 100m;

    private static string CustomerReference(string userId) => $"{CustomerReferencePrefix}{userId}";

    /// <summary>
    /// One home for the whole-call budget (a CancellationToken is the only bound that covers all
    /// retry attempts) and for translating SDK, transport and parse failures into
    /// <see cref="SubscriptionBillingException"/> with caller-safe messages.
    /// </summary>
    private async Task<T> CallAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TotalCallBudget);
        try
        {
            return await call(cts.Token);
        }
        catch (SubscriptionBillingException)
        {
            throw;
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRaw(ex.Error, ex);
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            // Case A operation: its typed error is not caught by the RawError arm above.
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw MapRaw(raw, ex);
            }
            throw SubscriptionBillingException.ProviderError(ex);
        }
        catch (JsonException ex)
        {
            // Two directions, same outcome here: an unreadable 2xx body (outcome unknown) or an
            // unreadable error body (rejected, reason lost). Both are provider-side failures the
            // caller cannot fix; neither may be presented as a domain absence.
            throw SubscriptionBillingException.ProviderError(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            if (ct.IsCancellationRequested)
            {
                throw; // the caller gave up — propagate their cancellation, not our error mapping
            }
            throw SubscriptionBillingException.ProviderError(ex);
        }
    }

    private static (string FirstName, string LastName) SplitName(string userName)
    {
        var local = string.IsNullOrEmpty(userName)
            ? "shopper"
            : (userName.Contains('@') ? userName[..userName.IndexOf('@')] : userName);

        var parts = local.Split(NameSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var first = Capitalize(parts.Length > 0 ? parts[0] : "eShop");
        var last = parts.Length > 1 ? Capitalize(string.Join(" ", parts.Skip(1))) : "Shopper";
        return (first, last);
    }

    private static string Capitalize(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];
}

