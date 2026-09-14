using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// <see cref="ISubscriptionBillingService"/> backed by Maxio Advanced Billing, via the
/// <c>AsadAli.AdvancedBilling.Sdk</c> client. Every Maxio interaction goes through the SDK client.
/// All SDK failures (typed errors, transport failures, and malformed responses) are translated into
/// <see cref="BillingException"/> so no SDK type or raw provider detail escapes this boundary.
/// </summary>
public class MaxioBillingService : ISubscriptionBillingService
{
    // States in which an existing subscription is considered still "live", so a repeat subscribe
    // request reuses it rather than creating a duplicate. Terminal states below are excluded.
    private static readonly HashSet<string> TerminalStates =
        new(StringComparer.OrdinalIgnoreCase) { "canceled", "expired", "trial_ended" };

    // Serializes subscribe operations per user reference so a double-click within a single process
    // instance cannot race past the find-or-create / find-before-subscribe idempotency checks.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscribeLocks = new();

    private readonly MaxioAdvancedBillingClient _client;
    private readonly ILogger<MaxioBillingService> _logger;
    private readonly MaxioSettings _settings;

    public MaxioBillingService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioBillingService> logger)
    {
        _client = client;
        _logger = logger;
        _settings = settings.Value;
    }

    public async Task<IReadOnlyCollection<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var familyHandle = _settings.ProductFamilyHandle;
        if (string.IsNullOrWhiteSpace(familyHandle))
            throw new BillingException("Billing is not configured: no product family handle is set.");

        var familyId = await ResolveProductFamilyIdAsync(familyHandle, cancellationToken);

        IReadOnlyList<ProductResponse> products;
        try
        {
            products = await _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: familyId.ToString(),
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
                ct: cancellationToken);
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            throw TranslateListProducts(ex);
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            throw Transport(ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw Malformed(ex);
        }

        return products
            .Select(p => p.Product)
            .Where(p => p is not null)
            .Select(p => MapPlan(p!, familyHandle))
            .ToList();
    }

    public async Task<CustomerSubscription> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.UserReference))
            throw new BillingException("A user reference is required to subscribe.", (int)HttpStatusCode.BadRequest);
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
            throw new BillingException("A plan handle is required to subscribe.", (int)HttpStatusCode.BadRequest);

        var gate = SubscribeLocks.GetOrAdd(request.UserReference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customerId = await FindOrCreateCustomerAsync(request, cancellationToken);

            // Idempotency: if the shopper already holds a live subscription to this plan, return it
            // instead of enrolling them again.
            var existing = await ListCustomerSubscriptionsAsync(customerId, cancellationToken);
            var reuse = existing.FirstOrDefault(s =>
                string.Equals(s.Product?.Handle, request.PlanHandle, StringComparison.OrdinalIgnoreCase)
                && !IsTerminal(s.State?.Value));
            if (reuse is not null)
            {
                _logger.LogInformation(
                    "Reusing existing subscription {SubscriptionId} for {Reference} on plan {Plan} (idempotent).",
                    reuse.Id, request.UserReference, request.PlanHandle);
                return MapSubscription(reuse, request.UserReference);
            }

            var created = await CreateSubscriptionAsync(customerId, request.PlanHandle, cancellationToken);
            _logger.LogInformation(
                "Created subscription {SubscriptionId} for {Reference} on plan {Plan}.",
                created.Id, request.UserReference, request.PlanHandle);
            return MapSubscription(created, request.UserReference);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyCollection<CustomerSubscription>> GetSubscriptionsAsync(string userReference, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userReference))
            throw new BillingException("A user reference is required.", (int)HttpStatusCode.BadRequest);

        var customerId = await FindCustomerIdByReferenceAsync(userReference, cancellationToken);
        if (customerId is null)
            return Array.Empty<CustomerSubscription>();

        var subscriptions = await ListCustomerSubscriptionsAsync(customerId.Value, cancellationToken);
        return subscriptions.Select(s => MapSubscription(s, userReference)).ToList();
    }

    // ---- Maxio operations (each translates SDK failures into BillingException) ----

    private async Task<int> ResolveProductFamilyIdAsync(string familyHandle, CancellationToken ct)
    {
        IReadOnlyList<ProductFamilyResponse> families;
        try
        {
            families = await _client.ProductFamilies.ListProductFamilies(
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                ct: ct);
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRaw(ex, "list product families");
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            throw Transport(ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw Malformed(ex);
        }

        var match = families
            .Select(f => f.ProductFamily)
            .FirstOrDefault(f => f is not null
                && string.Equals(f.Handle, familyHandle, StringComparison.OrdinalIgnoreCase));

        if (match?.Id is null)
            throw new BillingException(
                $"No product family with handle '{familyHandle}' exists on this Maxio site.",
                (int)HttpStatusCode.NotFound);

        return match.Id.Value;
    }

    private async Task<int?> FindCustomerIdByReferenceAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference, ct);
            return response.Customer?.Id;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null; // exact-match lookup miss — not an error, the customer simply doesn't exist yet
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRaw(ex, "look up customer");
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            throw Transport(ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw Malformed(ex);
        }
    }

    private async Task<int> FindOrCreateCustomerAsync(SubscribeRequest request, CancellationToken ct)
    {
        var existing = await FindCustomerIdByReferenceAsync(request.UserReference, ct);
        if (existing is not null)
            return existing.Value;

        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = FirstNameOrDefault(request),
                LastName = string.IsNullOrWhiteSpace(request.LastName) ? "Subscriber" : request.LastName!,
                Email = string.IsNullOrWhiteSpace(request.Email) ? request.UserReference : request.Email,
                Reference = request.UserReference
            }
        };

        try
        {
            var response = await _client.Customers.CreateCustomer(body, ct);
            var id = response.Customer?.Id
                ?? throw Malformed(new InvalidOperationException("CreateCustomer returned no customer id."));
            return id;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // A transport retry can land a second create for a reference the first attempt already
            // created, surfacing as a 422. Reconcile by re-reading: if the customer now exists, the
            // create actually succeeded and we stay idempotent.
            var reconciled = await FindCustomerIdByReferenceAsync(request.UserReference, ct);
            if (reconciled is not null)
                return reconciled.Value;
            throw TranslateCreateCustomer(ex);
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            // The create may have reached Maxio before the socket failed. Reconcile before giving up.
            var reconciled = await FindCustomerIdByReferenceAsync(request.UserReference, ct);
            if (reconciled is not null)
                return reconciled.Value;
            throw Transport(ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw Malformed(ex);
        }
    }

    private async Task<Subscription> CreateSubscriptionAsync(int customerId, string planHandle, CancellationToken ct)
    {
        var body = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = planHandle,
                CustomerId = customerId,
                // Invoice/remittance collection so no payment method is needed. The default (Automatic)
                // would try to charge a card and 422 on a balance-bearing plan with no card on file.
                PaymentCollectionMethod = ResolveCollectionMethod(_settings.PaymentCollectionMethod)
                // No payment attributes / payment_profile_id: the shopper subscribes without a card.
            }
        };

        try
        {
            var response = await _client.Subscriptions.CreateSubscription(body, ct);
            return response.Subscription
                ?? throw Malformed(new InvalidOperationException("CreateSubscription returned no subscription."));
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            throw TranslateCreateSubscription(ex);
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            throw Transport(ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw Malformed(ex);
        }
    }

    private async Task<IReadOnlyList<Subscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct)
    {
        try
        {
            var responses = await _client.Customers.ListCustomerSubscriptions(customerId, ct);
            return responses
                .Select(r => r.Subscription)
                .Where(s => s is not null)
                .Select(s => s!)
                .ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRaw(ex, "list customer subscriptions");
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            throw Transport(ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw Malformed(ex);
        }
    }

    // ---- Mapping ----

    private static SubscriptionPlan MapPlan(Product p, string familyHandle) => new()
    {
        Handle = p.Handle ?? string.Empty,
        Id = p.Id?.ToString() ?? string.Empty,
        Name = p.Name ?? string.Empty,
        Description = p.Description,
        PriceInCents = p.PriceInCents ?? 0,
        Interval = p.Interval ?? 0,
        IntervalUnit = p.IntervalUnit?.Value,
        ProductFamilyHandle = p.ProductFamily?.Handle ?? familyHandle
    };

    private static CustomerSubscription MapSubscription(Subscription s, string reference)
    {
        // Per the Maxio contract, the "next billing date" is reported as current_period_ends_at;
        // next_billing_at exists only on the request, never the response.
        var product = s.Product;
        return new CustomerSubscription
        {
            Id = s.Id?.ToString() ?? string.Empty,
            State = s.State?.Value ?? "unknown",
            PlanHandle = product?.Handle ?? string.Empty,
            PlanName = product?.Name ?? string.Empty,
            PriceInCents = product?.PriceInCents ?? s.ProductPriceInCents ?? 0,
            Interval = product?.Interval ?? 0,
            IntervalUnit = product?.IntervalUnit?.Value,
            NextBillingDate = s.CurrentPeriodEndsAt,
            CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
            CreatedAt = s.CreatedAt,
            CustomerReference = reference
        };
    }

    // ---- Helpers ----

    private static bool IsTerminal(string? state) =>
        !string.IsNullOrEmpty(state) && TerminalStates.Contains(state);

    private static string FirstNameOrDefault(SubscribeRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.FirstName))
            return request.FirstName!;
        var email = request.Email;
        var at = email.IndexOf('@');
        if (at > 0)
            return email[..at];
        return string.IsNullOrWhiteSpace(email) ? "eShop" : email;
    }

    private static CollectionMethod ResolveCollectionMethod(string? configured) =>
        (configured?.Trim().ToLowerInvariant()) switch
        {
            "invoice" => CollectionMethod.Invoice,
            "automatic" => CollectionMethod.Automatic,
            "prepaid" => CollectionMethod.Prepaid,
            _ => CollectionMethod.Remittance
        };

    private static bool IsTransport(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or OperationCanceledException;

    private BillingException TranslateRaw(SdkException<RawError> ex, string action) =>
        TranslateRaw(ex.Error, action, ex);

    private BillingException TranslateRaw(RawError raw, string action, Exception inner)
    {
        var status = (int)raw.StatusCode;
        _logger.LogWarning("Maxio rejected '{Action}' with HTTP {Status}.", action, status);
        return new BillingException(
            $"The billing provider rejected the request to {action} (HTTP {status}).", status, inner);
    }

    // CreateSubscription's typed 422 carries a required list of human-readable validation strings
    // (e.g. "No payment method was on file ...") — surface them; they are caller-actionable.
    private BillingException TranslateCreateSubscription(SdkException<CreateSubscriptionError> ex)
    {
        if (ex.Error.TryGetErrorListResponse1(out var list) && list.Errors.Count > 0)
        {
            var detail = string.Join("; ", list.Errors);
            _logger.LogWarning("Maxio rejected 'create subscription' (422): {Detail}", detail);
            return new BillingException(
                $"The billing provider rejected the subscription: {detail}",
                (int)HttpStatusCode.UnprocessableEntity, ex);
        }
        if (ex.Error.TryGetRawError(out var raw))
            return TranslateRaw(raw, "create subscription", ex);
        _logger.LogWarning("Maxio rejected 'create subscription' with an unrecognized error shape.");
        return new BillingException(
            "The billing provider rejected the subscription.", (int)HttpStatusCode.UnprocessableEntity, ex);
    }

    // CreateCustomer's typed error model only maps per_page/price_point keys, so the real message for a
    // customer 422 (email/reference/etc.) lives in the raw body — read that for the log.
    private BillingException TranslateCreateCustomer(SdkException<CreateCustomerError> ex)
    {
        if (ex.Error.TryGetRawError(out var raw))
        {
            var status = (int)raw.StatusCode;
            string body;
            try { body = raw.ReadAsString(); } catch { body = "<unreadable>"; }
            _logger.LogWarning("Maxio rejected 'create customer' (HTTP {Status}): {Body}", status, body);
            return new BillingException("The billing provider rejected the customer.", status, ex);
        }
        _logger.LogWarning("Maxio rejected 'create customer' with an unrecognized error shape.");
        return new BillingException(
            "The billing provider rejected the customer.", (int)HttpStatusCode.UnprocessableEntity, ex);
    }

    private BillingException TranslateListProducts(SdkException<ListProductsForProductFamilyError> ex)
    {
        if (ex.Error.TryGetString(out var message))
        {
            _logger.LogWarning("Maxio rejected 'list plans': {Message}", message);
            return new BillingException("The billing provider could not list plans.", (int)HttpStatusCode.NotFound, ex);
        }
        if (ex.Error.TryGetRawError(out var raw))
            return TranslateRaw(raw, "list plans", ex);
        return new BillingException("The billing provider could not list plans.", (int)HttpStatusCode.BadGateway, ex);
    }

    private BillingException Transport(Exception ex)
    {
        _logger.LogError(ex, "The billing provider was unreachable.");
        return new BillingException(
            "The billing provider is currently unreachable. Please try again shortly.",
            (int)HttpStatusCode.BadGateway, ex);
    }

    private BillingException Malformed(Exception ex)
    {
        _logger.LogError(ex, "The billing provider returned an unreadable response.");
        return new BillingException(
            "The billing provider returned a response that could not be processed.",
            (int)HttpStatusCode.BadGateway, ex);
    }
}
