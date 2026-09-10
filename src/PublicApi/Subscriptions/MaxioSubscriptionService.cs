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
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// Integration layer over the Maxio Advanced Billing SDK. Every Maxio call flows through
/// <see cref="Bounded{T}"/> (which enforces a whole-call deadline and converts transport / decode
/// failures into <see cref="MaxioApiException"/>); provider error responses are translated per
/// operation. The service is a singleton and holds the per-user locks that make the subscribe
/// flow idempotent against double-clicks.
/// </summary>
public sealed class MaxioSubscriptionService : IMaxioSubscriptionService
{
    // Subscription states in which the customer is considered already subscribed to a product.
    private static readonly SubscriptionState[] DeadStates =
    {
        SubscriptionState.Canceled, SubscriptionState.Expired, SubscriptionState.FailedToCreate
    };

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionService> _logger;
    private readonly TimeSpan _budget;

    // One lock per customer reference: serializes ensure-customer + dedupe + create so concurrent
    // requests (double-click) never create two customers or two live subscriptions.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
        _budget = TimeSpan.FromSeconds(Math.Max(1, _settings.RequestTimeoutSeconds));
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken ct)
    {
        var products = await ListFamilyProductsAsync(ct);
        return products.Select(MapPlan).ToList();
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(string userName, CancellationToken ct)
    {
        var reference = BuildReference(userName);
        var customer = await FindCustomerAsync(reference, ct);
        if (customer?.Id is null)
        {
            // No Maxio customer yet ⇒ no subscriptions. Not an error.
            return Array.Empty<SubscriptionDto>();
        }

        var subs = await ListCustomerSubscriptionsAsync(customer.Id.Value, ct);
        return subs.Select(MapSubscription).ToList();
    }

    public async Task<SubscribeOutcome> SubscribeAsync(string userName, string? planHandle, CancellationToken ct)
    {
        var reference = BuildReference(userName);
        var gate = _locks.GetOrAdd(reference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            // 1. Resolve & validate the requested plan against the configured family (catalog-agnostic).
            var products = await ListFamilyProductsAsync(ct);
            var available = string.Join(", ", products.Select(p => p.Handle).Where(h => h is not null));

            if (string.IsNullOrWhiteSpace(planHandle))
            {
                throw new MaxioApiException(400,
                    $"'planHandle' is required. Available plans: {available}.");
            }

            var product = products.FirstOrDefault(p =>
                string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
            if (product is null)
            {
                throw new MaxioApiException(400,
                    $"Unknown plan handle '{planHandle}'. Available plans: {available}.");
            }

            // 2. Ensure the Maxio customer exists (idempotent by reference).
            var customer = await EnsureCustomerAsync(userName, reference, ct);
            var customerId = customer.Id ?? throw new MaxioApiException(502, "Maxio returned a customer without an id.");

            // 3. Dedupe: already a live subscription to this product? Return it.
            var existing = await FindLiveSubscriptionAsync(customerId, product.Id, ct);
            if (existing is not null)
            {
                _logger.LogInformation(
                    "Subscribe: customer {CustomerId} already has live subscription {SubscriptionId} to plan {PlanHandle}.",
                    customerId, existing.Id, product.Handle);
                return new SubscribeOutcome(MapSubscription(existing), AlreadySubscribed: true);
            }

            // 4. Create the subscription.
            var created = await CreateSubscriptionAsync(product.Handle!, customerId, ct);
            _logger.LogInformation(
                "Subscribe: created subscription {SubscriptionId} for customer {CustomerId} on plan {PlanHandle} (state {State}).",
                created.Id, customerId, product.Handle, created.State?.Value);
            return new SubscribeOutcome(MapSubscription(created), AlreadySubscribed: false);
        }
        finally
        {
            gate.Release();
        }
    }

    // ---- SDK operations (each wrapped for transport/decode; provider errors translated) ----

    private async Task<IReadOnlyList<Product>> ListFamilyProductsAsync(CancellationToken ct)
    {
        try
        {
            var response = await Bounded(c => _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: FamilyPathId(_settings.ProductFamilyHandle),
                dateField: null,
                filter: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                includeArchived: false,
                include: null,
                ct: c), ct);

            return response.Select(r => r.Product).Where(p => p is not null).Select(p => p!).ToList();
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out var notFound))
            {
                throw new MaxioApiException(404,
                    $"Product family '{_settings.ProductFamilyHandle}' was not found in Maxio. {notFound}");
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw ToApiException(raw, "listing subscription plans");
            }
            throw new MaxioApiException(502, "Maxio returned an unrecognized error listing subscription plans.", ex);
        }
    }

    private async Task<Customer?> FindCustomerAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await Bounded(c => _client.Customers.ReadCustomerByReference(reference, ct: c), ct);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw ToApiException(ex.Error, "looking up the billing customer");
        }
    }

    private async Task<Customer> EnsureCustomerAsync(string userName, string reference, CancellationToken ct)
    {
        var existing = await FindCustomerAsync(reference, ct);
        if (existing is not null)
        {
            _logger.LogInformation("Ensure customer: found existing Maxio customer {CustomerId} for reference {Reference}.",
                existing.Id, reference);
            return existing;
        }

        var (firstName, lastName, email) = DeriveIdentity(userName);
        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference
            }
        };

        try
        {
            var response = await Bounded(c => _client.Customers.CreateCustomer(body, ct: c), ct);
            _logger.LogInformation("Ensure customer: created Maxio customer {CustomerId} for reference {Reference}.",
                response.Customer.Id, reference);
            return response.Customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                // Typed 422 body; its shape is a union, so surface the raw body for the message.
                var detail = ex.Error.TryGetRawError(out var typedRaw) ? SafeBody(typedRaw) : null;
                throw new MaxioApiException(422,
                    $"Maxio rejected the customer details.{(detail is null ? "" : " " + detail)}", ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw ToApiException(raw, "creating the billing customer");
            }
            throw new MaxioApiException(502, "Maxio returned an unrecognized error creating the billing customer.", ex);
        }
    }

    private async Task<IReadOnlyList<Subscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct)
    {
        try
        {
            var response = await Bounded(c => _client.Customers.ListCustomerSubscriptions(customerId, ct: c), ct);
            return response.Select(r => r.Subscription).Where(s => s is not null).Select(s => s!).ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw ToApiException(ex.Error, "listing your subscriptions");
        }
    }

    private async Task<Subscription?> FindLiveSubscriptionAsync(int customerId, int? productId, CancellationToken ct)
    {
        var subs = await ListCustomerSubscriptionsAsync(customerId, ct);
        return subs.FirstOrDefault(s =>
            (productId is null || s.Product?.Id == productId) && IsLive(s.State));
    }

    private async Task<Subscription> CreateSubscriptionAsync(string productHandle, int customerId, CancellationToken ct)
    {
        var body = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = productHandle,
                CustomerId = customerId,
                // Invoice/remittance collection (configurable) so the subscription activates without
                // a captured payment method. Automatic collection would attempt to charge the
                // balance immediately and fail with "No payment method was on file".
                PaymentCollectionMethod = string.IsNullOrWhiteSpace(_settings.PaymentCollectionMethod)
                    ? null
                    : CollectionMethod.FromValue(_settings.PaymentCollectionMethod)
            }
        };

        try
        {
            var response = await Bounded(c => _client.Subscriptions.CreateSubscription(body, ct: c), ct);
            return response.Subscription
                ?? throw new MaxioApiException(502, "Maxio created the subscription but returned no subscription body.");
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errors))
            {
                var joined = string.Join("; ", errors.Errors);
                throw new MaxioApiException(422,
                    $"Maxio could not create the subscription: {joined}", ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw ToApiException(raw, "creating the subscription");
            }
            throw new MaxioApiException(502, "Maxio returned an unrecognized error creating the subscription.", ex);
        }
    }

    // ---- Whole-call bound + transport / decode failure translation ----

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_budget);
        try
        {
            return await call(cts.Token);
        }
        catch (OperationCanceledException ex)
        {
            // Caller aborted (client disconnected) ⇒ propagate; our budget elapsed ⇒ gateway timeout.
            if (ct.IsCancellationRequested)
            {
                throw;
            }
            throw new MaxioApiException(504, "The billing provider did not respond in time.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new MaxioApiException(502, "The billing provider is unreachable.", ex);
        }
        catch (JsonException ex)
        {
            // A drifted 2xx body, or a non-2xx body that didn't match its typed error shape (which
            // replaces the SdkException). Either way the outcome/detail is unusable here.
            throw new MaxioApiException(502, "The billing provider returned a response that could not be processed.", ex);
        }
    }

    private MaxioApiException ToApiException(RawError raw, string action)
    {
        var provider = (int)raw.StatusCode;
        var outward = provider switch
        {
            401 or 403 => 502, // our credentials — not the caller's fault
            429 => 503,        // our quota
            >= 400 and < 500 => provider,
            _ => 502
        };

        // Only echo the provider body for genuine caller-facing 4xx; keep 5xx opaque.
        string message = outward is >= 400 and < 500
            ? $"Maxio rejected the request while {action}. {SafeBody(raw)}".Trim()
            : $"The billing provider failed while {action}.";

        _logger.Log(outward >= 500 ? LogLevel.Error : LogLevel.Warning,
            "Maxio error while {Action}: HTTP {Provider} -> {Outward}", action, provider, outward);

        return new MaxioApiException(outward, message);
    }

    private static string SafeBody(RawError raw)
    {
        string body;
        try
        {
            body = raw.ReadAsString();
        }
        catch
        {
            return string.Empty;
        }
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }
        body = body.Trim();
        return body.Length > 500 ? body.Substring(0, 500) : body;
    }

    // ---- Mapping & helpers ----

    private static bool IsLive(SubscriptionState? state) =>
        state is not null && !DeadStates.Contains(state);

    private static SubscriptionPlanDto MapPlan(Product p) => new()
    {
        Id = p.Id ?? 0,
        Handle = p.Handle,
        Name = p.Name,
        Description = p.Description,
        PriceInCents = p.PriceInCents ?? 0,
        Price = (p.PriceInCents ?? 0) / 100m,
        Interval = p.Interval,
        IntervalUnit = p.IntervalUnit?.Value,
        PricePointHandle = p.ProductPricePointHandle
    };

    private static SubscriptionDto MapSubscription(Subscription s)
    {
        var cents = s.ProductPriceInCents ?? s.CurrentBillingAmountInCents ?? 0;
        return new SubscriptionDto
        {
            Id = s.Id ?? 0,
            PlanHandle = s.Product?.Handle,
            PlanName = s.Product?.Name,
            State = s.State?.Value,
            PriceInCents = cents,
            Price = cents / 100m,
            NextBillingDate = s.NextAssessmentAt,
            CurrentPeriodEndsAt = s.CurrentPeriodEndsAt
        };
    }

    /// <summary>
    /// Formats the configured product family for the <c>{product_family_id}</c> path segment: a
    /// numeric id is passed as-is, a handle is prefixed with <c>handle:</c> (per the API contract),
    /// and an already-prefixed value is left untouched.
    /// </summary>
    private static string FamilyPathId(string value)
    {
        var v = value.Trim();
        if (v.StartsWith("handle:", StringComparison.OrdinalIgnoreCase) || v.All(char.IsDigit))
        {
            return v;
        }
        return "handle:" + v;
    }

    /// <summary>Stable, deterministic Maxio customer reference for an eShop user.</summary>
    private static string BuildReference(string userName) =>
        "eshoponweb:" + userName.Trim().ToLowerInvariant();

    private static (string FirstName, string LastName, string Email) DeriveIdentity(string userName)
    {
        var name = userName.Trim();
        var isEmail = name.Contains('@') && !name.StartsWith('@') && !name.EndsWith('@');
        var email = isEmail ? name : $"{Sanitize(name)}@users.eshoponweb.local";
        var local = email.Split('@')[0];
        var firstName = string.IsNullOrWhiteSpace(local) ? "eShop" : local;
        return (firstName, "(eShopOnWeb)", email);
    }

    private static string Sanitize(string value)
    {
        var chars = value.Where(ch => char.IsLetterOrDigit(ch) || ch is '.' or '-' or '_').ToArray();
        var cleaned = new string(chars);
        return string.IsNullOrEmpty(cleaned) ? "user" : cleaned;
    }
}
