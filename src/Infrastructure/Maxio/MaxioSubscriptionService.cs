using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// <see cref="ISubscriptionService"/> backed by the Maxio Advanced Billing SDK. All contract facts
/// (signatures, wire names, envelope shapes, error cases, enum wire values) come from the grounded
/// Maxio SDK contract sheet. Every provider call is wrapped so that only
/// <see cref="SubscriptionBillingException"/> crosses this boundary, carrying the provider status so
/// the API layer can map a provider 4xx to a client 4xx and reserve 5xx for outages.
/// </summary>
public class MaxioSubscriptionService : ISubscriptionService
{
    // Password is a literal per the Maxio Basic-auth scheme (API key is the username).
    // States that count as "already subscribed" for idempotent duplicate detection.
    private static readonly HashSet<string> SubscribedStates =
        new(StringComparer.OrdinalIgnoreCase) { "active", "trialing" };

    // Broader set used only when reconciling after a transport failure: a just-created subscription
    // may briefly be pending/assessing before it settles to active.
    private static readonly HashSet<string> LandedStates =
        new(StringComparer.OrdinalIgnoreCase) { "active", "trialing", "pending", "assessing", "soft_failure" };

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly KeyedAsyncLock _userLock;
    private readonly IAppLogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioSettings> settings,
        KeyedAsyncLock userLock,
        IAppLogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _userLock = userLock;
        _logger = logger;
    }

    // ---- ISubscriptionService --------------------------------------------------------------

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        using var cts = CreateBoundedCts(cancellationToken);
        var ct = cts.Token;

        var familyId = await ResolveProductFamilyIdAsync(ct);
        var products = await ListProductsAsync(familyId, ct);
        return products.Select(MapPlan).ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscriberInfo subscriber, string? productHandle, CancellationToken cancellationToken = default)
    {
        var handle = string.IsNullOrWhiteSpace(productHandle) ? _settings.DefaultProductHandle : productHandle.Trim();

        using var cts = CreateBoundedCts(cancellationToken);
        var ct = cts.Token;

        // Serialize per user so two concurrent requests (a double-click) cannot both pass the
        // find-before-create check and each enroll the customer.
        using (await _userLock.AcquireAsync(subscriber.Reference, ct))
        {
            var customer = await EnsureCustomerAsync(subscriber, ct);
            var customerId = customer.Id
                ?? throw new SubscriptionBillingException("Maxio returned a customer without an id.", 502);

            var existing = await FindSubscriptionAsync(customerId, handle, SubscribedStates, ct);
            if (existing is not null)
            {
                _logger.LogInformation("Subscribe: user {0} already active on plan {1}; returning existing subscription {2}.",
                    subscriber.Reference, handle, existing.Id ?? 0);
                return new SubscribeResult(MapSubscription(existing), AlreadyExisted: true);
            }

            var created = await CreateSubscriptionWithReconcileAsync(customerId, handle, ct);
            _logger.LogInformation("Subscribe: user {0} enrolled in plan {1} as subscription {2}.",
                subscriber.Reference, handle, created.Id ?? 0);
            return new SubscribeResult(MapSubscription(created), AlreadyExisted: false);
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(string customerReference, CancellationToken cancellationToken = default)
    {
        using var cts = CreateBoundedCts(cancellationToken);
        var ct = cts.Token;

        var customer = await FindCustomerByReferenceAsync(customerReference, ct);
        if (customer?.Id is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await ListCustomerSubscriptionsAsync(customer.Id.Value, ct);
        return subscriptions
            .Select(sr => sr.Subscription)
            .Where(s => s is not null)
            .Select(s => MapSubscription(s!))
            .ToList();
    }

    // ---- Plans -----------------------------------------------------------------------------

    private async Task<int> ResolveProductFamilyIdAsync(CancellationToken ct)
    {
        IReadOnlyList<ProductFamilyResponse> families;
        try
        {
            families = await _client.ProductFamilies.ListProductFamilies(
                dateField: null, startDate: null, endDate: null, startDatetime: null, endDatetime: null, ct: ct);
        }
        catch (SdkException<RawError> ex) { throw TranslateRaw(ex, "list product families"); }
        catch (Exception ex) when (IsTransport(ex)) { throw Unreachable(ex); }
        catch (JsonException ex) { throw Unreadable(ex); }

        var match = families.FirstOrDefault(f =>
            string.Equals(f.ProductFamily?.Handle, _settings.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase));

        var id = match?.ProductFamily?.Id;
        if (id is null)
        {
            throw new SubscriptionBillingException(
                $"Configured product family '{_settings.ProductFamilyHandle}' was not found in Maxio.", 404);
        }
        return id.Value;
    }

    private async Task<List<Product>> ListProductsAsync(int familyId, CancellationToken ct)
    {
        var products = new List<Product>();
        var page = 1;
        const int perPage = 100;

        while (true)
        {
            IReadOnlyList<ProductResponse> pageItems;
            try
            {
                pageItems = await _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: familyId.ToString(CultureInfo.InvariantCulture),
                    dateField: null, filter: null, startDate: null, endDate: null,
                    startDatetime: null, endDatetime: null, includeArchived: false,
                    include: null, page: page, perPage: perPage, ct: ct);
            }
            catch (SdkException<ListProductsForProductFamilyError> ex) { throw TranslateListProducts(ex); }
            catch (Exception ex) when (IsTransport(ex)) { throw Unreachable(ex); }
            catch (JsonException ex) { throw Unreadable(ex); }

            // ProductResponse.Product is a required member: a drifted body missing it throws
            // JsonException at deserialize (caught above), so it is non-null here.
            products.AddRange(pageItems.Select(pr => pr.Product));

            if (pageItems.Count < perPage) break;
            page++;
        }

        return products;
    }

    // ---- Customers -------------------------------------------------------------------------

    private async Task<Customer> EnsureCustomerAsync(SubscriberInfo subscriber, CancellationToken ct)
    {
        var existing = await FindCustomerByReferenceAsync(subscriber.Reference, ct);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            return await CreateCustomerAsync(subscriber, ct);
        }
        catch (SubscriptionBillingException ex) when (ex.ProviderStatusCode == 422)
        {
            // Maxio enforces one customer per reference; a 422 here means a concurrent create won
            // the race. Re-read by reference and use that customer instead of duplicating.
            var afterRace = await FindCustomerByReferenceAsync(subscriber.Reference, ct);
            if (afterRace is not null)
            {
                _logger.LogWarning("EnsureCustomer: create for reference {0} lost a race; reusing existing customer {1}.",
                    subscriber.Reference, afterRace.Id ?? 0);
                return afterRace;
            }
            throw;
        }
    }

    private async Task<Customer?> FindCustomerByReferenceAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference, ct);
            return response.Customer; // required member -> non-null on a 2xx
        }
        catch (SdkException<RawError> ex) when ((int)ex.Error.StatusCode == 404)
        {
            return null; // no customer for this reference yet
        }
        catch (SdkException<RawError> ex) { throw TranslateRaw(ex, "look up the customer"); }
        catch (Exception ex) when (IsTransport(ex)) { throw Unreachable(ex); }
        catch (JsonException ex) { throw Unreadable(ex); }
    }

    private async Task<Customer> CreateCustomerAsync(SubscriberInfo subscriber, CancellationToken ct)
    {
        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = subscriber.FirstName,
                LastName = subscriber.LastName,
                Email = subscriber.Email,
                Reference = subscriber.Reference
            }
        };

        try
        {
            var response = await _client.Customers.CreateCustomer(body, ct);
            return response.Customer;
        }
        catch (SdkException<CreateCustomerError> ex) { throw TranslateCreateCustomer(ex); }
        catch (Exception ex) when (IsTransport(ex)) { throw Unreachable(ex); }
        catch (JsonException ex) { throw Unreadable(ex); }
    }

    // ---- Subscriptions ---------------------------------------------------------------------

    private async Task<Subscription> CreateSubscriptionWithReconcileAsync(int customerId, string productHandle, CancellationToken ct)
    {
        var body = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = productHandle,
                CustomerId = customerId,
                // Bill a paid plan by invoice/remittance rather than an auto-charge (which would need a
                // card); the configured plans require no payment method, so no payment profile is sent.
                PaymentCollectionMethod = ParseCollectionMethod(_settings.PaymentCollectionMethod),
                NetTerms = string.IsNullOrWhiteSpace(_settings.NetTerms) ? null : _settings.NetTerms
            }
        };

        try
        {
            var response = await _client.Subscriptions.CreateSubscription(body, ct);

            // SubscriptionResponse.Subscription is nullable even on a 2xx. Never treat a null payload
            // as success: reconcile to see whether the write actually landed, else fail deterministically.
            if (response.Subscription is null)
            {
                var reconciled = await ReconcileCreatedSubscriptionAsync(customerId, productHandle);
                if (reconciled is not null) return reconciled;
                throw new SubscriptionBillingException(
                    "Maxio accepted the subscription request but returned no subscription.", 502);
            }

            return response.Subscription;
        }
        catch (SdkException<CreateSubscriptionError> ex) { throw TranslateCreateSubscription(ex); }
        catch (Exception ex) when (IsTransport(ex))
        {
            // A transport failure on a POST may have reached Maxio before failing (and the SDK can
            // resend an HttpRequestException on any verb). Reconcile: if a matching subscription now
            // exists, the write landed — return it rather than reporting an outage.
            var reconciled = await ReconcileCreatedSubscriptionAsync(customerId, productHandle);
            if (reconciled is not null)
            {
                _logger.LogWarning("CreateSubscription: transport failure for customer {0}, but reconcile found subscription {1}.",
                    customerId, reconciled.Id ?? 0);
                return reconciled;
            }
            throw Unreachable(ex);
        }
        catch (JsonException ex) { throw Unreadable(ex); }
    }

    /// <summary>
    /// After a create whose outcome is unknown, re-read the customer's subscriptions on a fresh,
    /// short-lived token (the original may be cancelled) to determine whether the write landed.
    /// Any reconcile failure is swallowed so the caller reports the original create failure.
    /// </summary>
    private async Task<Subscription?> ReconcileCreatedSubscriptionAsync(int customerId, string productHandle)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            return await FindSubscriptionAsync(customerId, productHandle, LandedStates, cts.Token);
        }
        catch
        {
            return null;
        }
    }

    private async Task<Subscription?> FindSubscriptionAsync(int customerId, string productHandle, HashSet<string> states, CancellationToken ct)
    {
        var subscriptions = await ListCustomerSubscriptionsAsync(customerId, ct);
        foreach (var sr in subscriptions)
        {
            var s = sr.Subscription;
            if (s is null) continue;
            if (!string.Equals(s.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase)) continue;

            var state = s.State?.Value;
            if (state is not null && states.Contains(state))
            {
                return s;
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
        catch (SdkException<RawError> ex) { throw TranslateRaw(ex, "list the customer's subscriptions"); }
        catch (Exception ex) when (IsTransport(ex)) { throw Unreachable(ex); }
        catch (JsonException ex) { throw Unreadable(ex); }
    }

    // ---- Mapping ---------------------------------------------------------------------------

    private static SubscriptionPlan MapPlan(Product p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        Handle = p.Handle,
        PriceInCents = p.PriceInCents,
        FormattedPrice = FormatPrice(p.PriceInCents),
        Interval = p.Interval,
        IntervalUnit = p.IntervalUnit?.Value,
        Description = p.Description
    };

    private static CustomerSubscription MapSubscription(Subscription s)
    {
        var priceInCents = s.ProductPriceInCents ?? s.Product?.PriceInCents;
        return new CustomerSubscription
        {
            Id = s.Id,
            State = s.State?.Value,
            ProductHandle = s.Product?.Handle,
            ProductName = s.Product?.Name,
            PriceInCents = priceInCents,
            FormattedPrice = FormatPrice(priceInCents),
            // The Subscription model has no next_billing_at; current_period_ends_at is the documented
            // "next billing date", with next_assessment_at as a fallback.
            NextBillingDate = s.CurrentPeriodEndsAt ?? s.NextAssessmentAt,
            CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
            CustomerReference = s.Customer?.Reference,
            CustomerId = s.Customer?.Id
        };
    }

    private static string? FormatPrice(long? priceInCents)
    {
        if (priceInCents is null) return null;
        var amount = priceInCents.Value / 100m;
        return "$" + amount.ToString("N2", CultureInfo.InvariantCulture);
    }

    // ---- Error translation -----------------------------------------------------------------

    private static CollectionMethod? ParseCollectionMethod(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "" => null,
        "remittance" => CollectionMethod.Remittance,
        "invoice" => CollectionMethod.Invoice,
        "automatic" => CollectionMethod.Automatic,
        "prepaid" => CollectionMethod.Prepaid,
        _ => throw new SubscriptionBillingException(
            $"Unsupported Maxio:PaymentCollectionMethod '{value}'. Use remittance, invoice, automatic, or prepaid.", 500)
    };

    private static bool IsTransport(Exception ex) => ex is HttpRequestException or TaskCanceledException;

    private static SubscriptionBillingException Unreachable(Exception ex) =>
        new("The billing provider could not be reached. Please try again shortly.", 502, ex);

    private static SubscriptionBillingException Unreadable(Exception ex) =>
        new("The billing provider returned a response that could not be processed.", 502, ex);

    private static SubscriptionBillingException TranslateRaw(SdkException<RawError> ex, string action)
    {
        var status = (int)ex.Error.StatusCode;
        return new SubscriptionBillingException(
            $"Maxio request failed while trying to {action} (HTTP {status}).{RawDetail(ex.Error)}", status, ex);
    }

    private static SubscriptionBillingException TranslateCreateCustomer(SdkException<CreateCustomerError> ex)
    {
        if (ex.Error.TryGetCustomerErrorResponse1(out var typed))
        {
            return new SubscriptionBillingException($"Maxio rejected the customer.{Detail(typed)}", 422, ex);
        }
        if (ex.Error.TryGetRawError(out var raw))
        {
            return new SubscriptionBillingException(
                $"Maxio customer create failed (HTTP {(int)raw.StatusCode}).{RawDetail(raw)}", (int)raw.StatusCode, ex);
        }
        return new SubscriptionBillingException("Maxio customer create failed.", null, ex);
    }

    private static SubscriptionBillingException TranslateCreateSubscription(SdkException<CreateSubscriptionError> ex)
    {
        if (ex.Error.TryGetErrorListResponse1(out var typed))
        {
            return new SubscriptionBillingException($"Maxio rejected the subscription.{Detail(typed)}", 422, ex);
        }
        if (ex.Error.TryGetRawError(out var raw))
        {
            return new SubscriptionBillingException(
                $"Maxio subscription create failed (HTTP {(int)raw.StatusCode}).{RawDetail(raw)}", (int)raw.StatusCode, ex);
        }
        return new SubscriptionBillingException("Maxio subscription create failed.", null, ex);
    }

    private static SubscriptionBillingException TranslateListProducts(SdkException<ListProductsForProductFamilyError> ex)
    {
        if (ex.Error.TryGetString(out var message))
        {
            return new SubscriptionBillingException($"Maxio could not list products (HTTP 404): {Truncate(message)}", 404, ex);
        }
        if (ex.Error.TryGetRawError(out var raw))
        {
            return new SubscriptionBillingException(
                $"Maxio product listing failed (HTTP {(int)raw.StatusCode}).{RawDetail(raw)}", (int)raw.StatusCode, ex);
        }
        return new SubscriptionBillingException("Maxio product listing failed.", null, ex);
    }

    private static string Detail(object? providerErrorBody)
    {
        var json = SafeSerialize(providerErrorBody);
        return string.IsNullOrWhiteSpace(json) ? string.Empty : $" {Truncate(json)}";
    }

    private static string RawDetail(RawError raw)
    {
        string body;
        try { body = raw.ReadAsString(); }
        catch { body = string.Empty; }
        return string.IsNullOrWhiteSpace(body) ? string.Empty : $" {Truncate(body)}";
    }

    private static string SafeSerialize(object? value)
    {
        try { return value is null ? string.Empty : JsonSerializer.Serialize(value); }
        catch { return string.Empty; }
    }

    private static string Truncate(string s) => s.Length <= 500 ? s : s.Substring(0, 500) + "…";

    private CancellationTokenSource CreateBoundedCts(CancellationToken outer)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(outer);
        cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _settings.RequestTimeoutSeconds)));
        return cts;
    }
}
