using System;
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
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Maxio Advanced Billing adapter for <see cref="ISubscriptionBillingService"/>. Translates the eShopOnWeb
/// domain onto the Maxio SDK and normalises every SDK failure (typed errors, raw errors, unparseable bodies,
/// transport failures) into <see cref="BillingException"/> carrying a caller-safe message and HTTP status.
/// </summary>
public class MaxioBillingService : ISubscriptionBillingService
{
    // States in which a subscription no longer counts as an active enrolment, so a fresh subscribe
    // should create a new one rather than being treated as a duplicate. Compared against wire values.
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create"
    };

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly IAppLogger<MaxioBillingService> _logger;

    public MaxioBillingService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioSettings> settings,
        IAppLogger<MaxioBillingService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyCollection<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var products = await ListProductsAsync(cancellationToken);
        return products
            .Select(pr => pr.Product)
            .Where(p => p is not null && !string.IsNullOrWhiteSpace(p.Handle))
            .Select(p => MapPlan(p!))
            .OrderBy(p => p.PriceInCents)
            .ToList();
    }

    public async Task<CustomerSubscription> SubscribeAsync(SubscriberIdentity subscriber, string planHandle, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        if (subscriber is null) throw new ArgumentNullException(nameof(subscriber));
        if (string.IsNullOrWhiteSpace(planHandle))
            throw new BillingException("A plan handle is required to subscribe.", 400);

        // 1. Confirm the plan belongs to the configured product family. Gives a clean 404 for an unknown
        //    handle and prevents subscribing to arbitrary products outside our catalog.
        var plans = await GetAvailablePlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null) throw new PlanNotFoundException(planHandle);

        // 2. Ensure a Maxio customer exists for this shopper (idempotent on the subscriber reference).
        var customer = await EnsureCustomerAsync(subscriber, cancellationToken);
        var customerId = customer.Id ?? throw new BillingException("Maxio returned a customer without an id.", 502);

        // 3. Idempotency: if the shopper already has a live subscription to this plan, return it instead
        //    of creating a duplicate (guards against double-clicks and retries).
        var existing = await ListSubscriptionsForCustomerAsync(customerId, subscriber.Reference, cancellationToken);
        var live = existing.FirstOrDefault(s =>
            string.Equals(s.PlanHandle, plan.Handle, StringComparison.OrdinalIgnoreCase) && IsLive(s.State));
        if (live is not null)
        {
            _logger.LogInformation("Subscriber {0} already subscribed to plan {1} (subscription {2}); returning existing.",
                subscriber.Reference, plan.Handle, live.Id);
            return live;
        }

        // 4. Enrol.
        var created = await CreateSubscriptionAsync(customerId, plan.Handle, subscriber.Reference, cancellationToken);
        _logger.LogInformation("Subscriber {0} enrolled in plan {1} (subscription {2}, state {3}).",
            subscriber.Reference, plan.Handle, created.Id, created.State);
        return created;
    }

    public async Task<IReadOnlyCollection<CustomerSubscription>> GetSubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        if (subscriber is null) throw new ArgumentNullException(nameof(subscriber));

        var customer = await TryReadCustomerAsync(subscriber.Reference, cancellationToken);
        if (customer?.Id is not int customerId)
        {
            // No billing customer has been provisioned yet, so there are no subscriptions.
            return Array.Empty<CustomerSubscription>();
        }

        return await ListSubscriptionsForCustomerAsync(customerId, subscriber.Reference, cancellationToken);
    }

    // ----- Products / plans -------------------------------------------------------------------------

    private async Task<IReadOnlyList<ProductResponse>> ListProductsAsync(CancellationToken ct)
    {
        try
        {
            try
            {
                return await ListProductsByFamilyAsync($"handle:{_settings.ProductFamilyHandle}", ct);
            }
            catch (SdkException<ListProductsForProductFamilyError> ex) when (IsNotFound(ex))
            {
                // The 'handle:' path form was not accepted for this operation — resolve the numeric
                // product-family id and retry with it.
                var familyId = await ResolveProductFamilyIdAsync(ct);
                return await ListProductsByFamilyAsync(familyId.ToString(CultureInfo.InvariantCulture), ct);
            }
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            throw Translate(ex, "list subscription plans");
        }
        catch (Exception ex) when (IsTransport(ex, ct)) { throw Unreachable(ex); }
        catch (JsonException ex) { throw Unprocessable(ex); }
    }

    private Task<IReadOnlyList<ProductResponse>> ListProductsByFamilyAsync(string productFamilyId, CancellationToken ct) =>
        _client.ProductFamilies.ListProductsForProductFamily(
            productFamilyId: productFamilyId,
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

    private async Task<int> ResolveProductFamilyIdAsync(CancellationToken ct)
    {
        IReadOnlyList<ProductFamilyResponse> families;
        try
        {
            families = await _client.ProductFamilies.ListProductFamilies(null, null, null, null, null, ct);
        }
        catch (SdkException<RawError> ex) { throw Translate(ex, "resolve the product family"); }
        catch (Exception ex) when (IsTransport(ex, ct)) { throw Unreachable(ex); }
        catch (JsonException ex) { throw Unprocessable(ex); }

        var match = families.FirstOrDefault(f =>
            string.Equals(f.ProductFamily?.Handle, _settings.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase));
        if (match?.ProductFamily?.Id is int id) return id;

        throw new BillingException($"Configured product family '{_settings.ProductFamilyHandle}' was not found in Maxio.", 404);
    }

    // ----- Customers --------------------------------------------------------------------------------

    private async Task<Customer> EnsureCustomerAsync(SubscriberIdentity subscriber, CancellationToken ct)
    {
        var existing = await TryReadCustomerAsync(subscriber.Reference, ct);
        if (existing is not null) return existing;

        var (firstName, lastName) = DeriveName(subscriber);
        try
        {
            var created = await _client.Customers.CreateCustomer(new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = subscriber.Email,
                    Reference = subscriber.Reference
                }
            }, ct);
            _logger.LogInformation("Created Maxio customer for subscriber {0}.", subscriber.Reference);
            return created.Customer ?? throw new BillingException("Maxio returned an empty customer.", 502);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // A concurrent request (double-click) may have created the customer first; recover idempotently.
            var recovered = await TryReadCustomerAsync(subscriber.Reference, ct);
            if (recovered is not null) return recovered;
            throw TranslateCreateCustomer(ex);
        }
        catch (Exception ex) when (IsTransport(ex, ct)) { throw Unreachable(ex); }
        catch (JsonException ex)
        {
            // Unparseable create response/error: recover if the customer now exists, else treat as a rejection.
            var recovered = await TryReadCustomerAsync(subscriber.Reference, ct);
            if (recovered is not null) return recovered;
            throw new BillingException("Maxio rejected the customer creation request.", 422, ex);
        }
    }

    private async Task<Customer?> TryReadCustomerAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference, ct);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null; // genuine miss — safe to create
        }
        catch (SdkException<RawError> ex) { throw Translate(ex, "look up the billing customer"); }
        catch (Exception ex) when (IsTransport(ex, ct)) { throw Unreachable(ex); }
        // Deliberately do NOT treat a JsonException as "not found": an unreadable response is an unknown
        // outcome, not a miss, and mapping it to absence could trigger a spurious customer create.
        catch (JsonException ex) { throw Unprocessable(ex); }
    }

    // ----- Subscriptions ----------------------------------------------------------------------------

    private async Task<CustomerSubscription> CreateSubscriptionAsync(int customerId, string productHandle, string customerReference, CancellationToken ct)
    {
        // This flow captures no card, so the subscription is billed by invoice rather than an automatic
        // charge (which would require a payment method on file and fail for a priced plan). Prefer the
        // Relationship-Invoicing collection method ("remittance"); fall back to the legacy Statements
        // value ("invoice") if the site rejects it. A 422 means nothing was created, so the retry is safe.
        try
        {
            return await CreateSubscriptionAsync(customerId, productHandle, customerReference, CollectionMethod.Remittance, ct);
        }
        catch (BillingException ex) when (ex.StatusCode == 422)
        {
            return await CreateSubscriptionAsync(customerId, productHandle, customerReference, CollectionMethod.Invoice, ct);
        }
    }

    private async Task<CustomerSubscription> CreateSubscriptionAsync(int customerId, string productHandle, string customerReference, CollectionMethod collectionMethod, CancellationToken ct)
    {
        try
        {
            var created = await _client.Subscriptions.CreateSubscription(new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = productHandle,
                    CustomerId = customerId,
                    PaymentCollectionMethod = collectionMethod
                }
            }, ct);
            var subscription = created.Subscription ?? throw new BillingException("Maxio returned an empty subscription.", 502);
            return MapSubscription(subscription, customerReference);
        }
        catch (SdkException<CreateSubscriptionError> ex) { throw TranslateCreateSubscription(ex); }
        catch (Exception ex) when (IsTransport(ex, ct)) { throw Unreachable(ex); }
        catch (JsonException ex)
        {
            // Per the SDK contract, a 422 whose body doesn't match the generated error shape surfaces as a
            // JsonException with the status lost. Treat a create-time parse failure as a rejection (422),
            // not an outage, so callers don't retry something that can never succeed.
            throw new BillingException("Maxio rejected the subscription request.", 422, ex);
        }
    }

    private async Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsForCustomerAsync(int customerId, string customerReference, CancellationToken ct)
    {
        try
        {
            var subscriptions = await _client.Customers.ListCustomerSubscriptions(customerId, ct);
            return subscriptions
                .Select(s => s.Subscription)
                .Where(s => s is not null)
                .Select(s => MapSubscription(s!, customerReference))
                .OrderByDescending(s => s.Id)
                .ToList();
        }
        catch (SdkException<RawError> ex) { throw Translate(ex, "list customer subscriptions"); }
        catch (Exception ex) when (IsTransport(ex, ct)) { throw Unreachable(ex); }
        catch (JsonException ex) { throw Unprocessable(ex); }
    }

    // ----- Mapping ----------------------------------------------------------------------------------

    private SubscriptionPlan MapPlan(Product product) => new()
    {
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? string.Empty,
        PriceInCents = product.PriceInCents ?? 0,
        FormattedPrice = FormatMoney(product.PriceInCents),
        Interval = FormatInterval(product.Interval, product.IntervalUnit),
        ProductFamilyHandle = product.ProductFamily?.Handle ?? _settings.ProductFamilyHandle ?? string.Empty
    };

    private static CustomerSubscription MapSubscription(Subscription subscription, string customerReference) => new()
    {
        Id = subscription.Id ?? 0,
        State = subscription.State?.Value ?? "unknown",
        PlanHandle = subscription.Product?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? string.Empty,
        PriceInCents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? 0,
        FormattedPrice = FormatMoney(subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents),
        NextBillingDate = subscription.CurrentPeriodEndsAt,
        CustomerReference = customerReference
    };

    private static string FormatMoney(long? cents)
    {
        var amount = (cents ?? 0) / 100m;
        return amount.ToString("C2", CultureInfo.GetCultureInfo("en-US"));
    }

    private static string FormatInterval(int? interval, IntervalUnit? unit)
    {
        var count = interval ?? 1;
        var unitLabel = unit?.Value ?? "month";
        return count == 1 ? $"1 {unitLabel}" : $"{count} {unitLabel}s";
    }

    private static (string firstName, string lastName) DeriveName(SubscriberIdentity subscriber)
    {
        if (!string.IsNullOrWhiteSpace(subscriber.FirstName) || !string.IsNullOrWhiteSpace(subscriber.LastName))
            return (Fallback(subscriber.FirstName, "eShop"), Fallback(subscriber.LastName, "Customer"));

        var source = subscriber.Email ?? subscriber.Reference ?? string.Empty;
        var at = source.IndexOf('@');
        var local = at > 0 ? source[..at] : source;
        var parts = local.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);
        var first = parts.Length > 0 ? Capitalize(parts[0]) : "eShop";
        var last = parts.Length > 1 ? Capitalize(parts[^1]) : "eShopOnWeb";
        return (first, last);

        static string Fallback(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value!;
        static string Capitalize(string value) => value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];
    }

    private static bool IsLive(string state) => !string.IsNullOrEmpty(state) && !TerminalStates.Contains(state);

    // ----- Configuration & error translation --------------------------------------------------------

    private void EnsureConfigured()
    {
        if (!_settings.IsConfigured)
        {
            throw new BillingException(
                "Maxio billing is not configured. Provide Maxio:ApiKey, Maxio:ProductFamilyHandle and " +
                "either Maxio:Subdomain or Maxio:BaseUrl.", 500);
        }
    }

    private static bool IsNotFound(SdkException<ListProductsForProductFamilyError> ex)
    {
        if (ex.Error.TryGetString(out _)) return true; // 404 body is delivered as a string
        if (ex.Error.TryGetRawError(out var raw)) return raw.StatusCode == HttpStatusCode.NotFound;
        return false;
    }

    private BillingException Translate(SdkException<RawError> ex, string action)
    {
        var status = (int)ex.Error.StatusCode;
        _logger.LogWarning("Maxio error while trying to {0}: HTTP {1}.", action, status);
        return new BillingException($"Maxio failed to {action} (HTTP {status}).{Detail(Safe(() => ex.Error.ReadAsString()))}",
            MapClientStatus(status), ex);
    }

    private BillingException Translate(SdkException<ListProductsForProductFamilyError> ex, string action)
    {
        if (ex.Error.TryGetString(out var body))
            return new BillingException($"Maxio failed to {action}.{Detail(body)}", 404, ex);
        if (ex.Error.TryGetRawError(out var raw))
        {
            var status = (int)raw.StatusCode;
            return new BillingException($"Maxio failed to {action} (HTTP {status}).{Detail(Safe(() => raw.ReadAsString()))}",
                MapClientStatus(status), ex);
        }
        return new BillingException($"Maxio failed to {action}.", 502, ex);
    }

    private BillingException TranslateCreateCustomer(SdkException<CreateCustomerError> ex)
    {
        if (ex.Error.TryGetCustomerErrorResponse1(out var typed))
        {
            var messages = new List<string>();
            if (typed?.Errors?.PerPage is { Count: > 0 } perPage) messages.AddRange(perPage);
            if (typed?.Errors?.PricePoint is { Count: > 0 } pricePoint) messages.AddRange(pricePoint);
            var detail = messages.Count > 0 ? string.Join("; ", messages) : null;
            return new BillingException($"Maxio rejected the customer.{Detail(detail)}", 422, ex);
        }
        if (ex.Error.TryGetRawError(out var raw))
        {
            var status = (int)raw.StatusCode;
            return new BillingException($"Maxio failed to create the customer (HTTP {status}).{Detail(Safe(() => raw.ReadAsString()))}",
                MapClientStatus(status), ex);
        }
        return new BillingException("Maxio failed to create the customer.", 502, ex);
    }

    private BillingException TranslateCreateSubscription(SdkException<CreateSubscriptionError> ex)
    {
        if (ex.Error.TryGetErrorListResponse1(out var errors) && errors?.Errors is { Count: > 0 } list)
            return new BillingException($"Maxio rejected the subscription.{Detail(string.Join("; ", list))}", 422, ex);
        if (ex.Error.TryGetRawError(out var raw))
        {
            var status = (int)raw.StatusCode;
            return new BillingException($"Maxio failed to create the subscription (HTTP {status}).{Detail(Safe(() => raw.ReadAsString()))}",
                MapClientStatus(status), ex);
        }
        return new BillingException("Maxio failed to create the subscription.", 502, ex);
    }

    // Provider 4xx the caller can act on stays a client 4xx; everything else is an upstream 502.
    private static int MapClientStatus(int providerStatus) => providerStatus switch
    {
        400 or 404 or 409 or 422 => providerStatus,
        _ => 502
    };

    private static string Detail(string? detail) => string.IsNullOrWhiteSpace(detail) ? string.Empty : $" {detail.Trim()}";

    private static string? Safe(Func<string> read)
    {
        try { return read(); }
        catch { return null; }
    }

    private BillingException Unreachable(Exception ex)
    {
        _logger.LogWarning("Maxio is unreachable: {0}", ex.Message);
        return new BillingException("The billing provider is currently unreachable. Please try again.", 503, ex);
    }

    private BillingException Unprocessable(Exception ex)
    {
        _logger.LogWarning("Maxio returned an unprocessable response: {0}", ex.Message);
        return new BillingException("Maxio returned a response that could not be processed.", 502, ex);
    }

    private static bool IsTransport(Exception ex, CancellationToken ct) =>
        ex is HttpRequestException
        || (ex is TaskCanceledException && !ct.IsCancellationRequested)
        || (ex is OperationCanceledException && !ct.IsCancellationRequested);
}
