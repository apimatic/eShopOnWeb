using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Maxio Advanced Billing adapter for <see cref="ISubscriptionBillingService"/>. This is the only
/// type that touches the Maxio SDK; it translates every SDK failure (API errors, transport
/// failures, and broken response bodies) into a caller-safe <see cref="BillingException"/>.
/// </summary>
public class MaxioBillingService : ISubscriptionBillingService
{
    // Per-user gate so concurrent "subscribe" calls for the same user (e.g. a double-click)
    // are serialized, making the read-then-create dedupe atomic within the process.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _subscribeGates = new();

    // Wire values, sourced from the SDK enum, of states in which a subscription no longer
    // occupies its plan and a re-subscribe is allowed.
    private static readonly HashSet<string> TerminalStates = new(StringComparer.Ordinal)
    {
        SubscriptionState.Canceled.Value,
        SubscriptionState.Expired.Value,
        SubscriptionState.FailedToCreate.Value,
        SubscriptionState.TrialEnded.Value,
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

    public async Task<IReadOnlyCollection<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var familyId = await ResolveProductFamilyIdAsync(cancellationToken);

        var plans = new List<SubscriptionPlan>();
        const int perPage = 100;
        var page = 1;
        while (true)
        {
            var products = await ListProductsAsync(familyId, page, perPage, cancellationToken);
            foreach (var pr in products)
            {
                var p = pr.Product;
                if (p is null)
                {
                    continue;
                }

                plans.Add(new SubscriptionPlan(
                    p.Handle ?? string.Empty,
                    p.Name ?? string.Empty,
                    p.Id ?? 0,
                    p.PriceInCents ?? 0,
                    p.Interval ?? 0,
                    p.IntervalUnit?.Value ?? string.Empty));
            }

            if (products.Count < perPage)
            {
                break;
            }

            page++;
        }

        return plans;
    }

    public async Task<CustomerSubscription> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            throw new BillingException("A plan handle is required to subscribe.", 400);
        }

        var gate = _subscribeGates.GetOrAdd(request.UserReference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(request, cancellationToken);
            var customerId = customer.Id ?? throw new BillingException("The billing provider returned a customer without an id.", 502);

            // create-subscription is NOT idempotent server-side; return an existing live
            // subscription to the same plan rather than creating a duplicate.
            var existing = await ListSubscriptionsForCustomerAsync(customerId, cancellationToken);
            var live = existing.FirstOrDefault(s =>
                string.Equals(s.PlanHandle, request.PlanHandle, StringComparison.Ordinal) && IsLive(s.State));
            if (live is not null)
            {
                _logger.LogInformation(
                    "Existing live subscription {0} to plan {1} found for customer {2}; skipping create.",
                    live.SubscriptionId, request.PlanHandle, customerId);
                return live;
            }

            var body = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    CustomerId = customerId,
                    ProductHandle = request.PlanHandle,
                    // These plans require no payment method, so bill by remittance (invoice) rather
                    // than attempting an automatic card charge — which lets signup succeed without a
                    // payment profile / card capture / 3-DS.
                    PaymentCollectionMethod = CollectionMethod.Remittance,
                },
            };

            CustomerSubscription subscription;
            try
            {
                subscription = await CreateSubscriptionRawAsync(body, cancellationToken);
            }
            catch (BillingException ex) when (ex.OutcomeUnknown)
            {
                // The create may have taken effect before the transport failed (or before a resend
                // was blocked). Reconcile against provider state rather than assume it did not.
                var after = await ListSubscriptionsForCustomerAsync(customerId, cancellationToken);
                var reconciled = after.FirstOrDefault(s =>
                    string.Equals(s.PlanHandle, request.PlanHandle, StringComparison.Ordinal) && IsLive(s.State));
                if (reconciled is null)
                {
                    throw;
                }

                _logger.LogWarning(
                    "Reconciled an unconfirmed subscription create for customer {0} to plan {1} as subscription {2}.",
                    customerId, request.PlanHandle, reconciled.SubscriptionId);
                return reconciled;
            }

            _logger.LogInformation(
                "Created subscription {0} to plan {1} for customer {2}.",
                subscription.SubscriptionId, request.PlanHandle, customerId);
            return subscription;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyCollection<CustomerSubscription>> ListSubscriptionsForUserAsync(string userReference, CancellationToken cancellationToken = default)
    {
        var customer = await TryReadCustomerAsync(userReference, cancellationToken);
        if (customer?.Id is not int id)
        {
            return Array.Empty<CustomerSubscription>();
        }

        return await ListSubscriptionsForCustomerAsync(id, cancellationToken);
    }

    // ---- customer -----------------------------------------------------------------------

    private async Task<Customer> EnsureCustomerAsync(SubscribeRequest req, CancellationToken ct)
    {
        var existing = await TryReadCustomerAsync(req.UserReference, ct);
        if (existing is not null)
        {
            return existing;
        }

        var (first, last) = DeriveName(req);
        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = first,
                LastName = last,
                Email = req.Email,
                Reference = req.UserReference,
            },
        };

        try
        {
            return await CreateCustomerRawAsync(body, ct);
        }
        catch (BillingException)
        {
            // A transport re-send or a concurrent create may already have created the customer
            // (the unique-reference constraint would then reject the second attempt). Reconcile
            // by re-reading before surfacing the failure.
            var reconciled = await TryReadCustomerAsync(req.UserReference, ct);
            if (reconciled is not null)
            {
                return reconciled;
            }

            throw;
        }
    }

    private async Task<Customer?> TryReadCustomerAsync(string reference, CancellationToken ct)
    {
        try
        {
            var resp = await _client.Customers.ReadCustomerByReference(reference, ct: ct);
            return resp?.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRaw("look up the customer", ex.Error, ex);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unreachable("look up the customer", ex);
        }
        catch (JsonException ex)
        {
            throw Unprocessable("look up the customer", ex);
        }
    }

    private async Task<Customer> CreateCustomerRawAsync(CreateCustomerRequest body, CancellationToken ct)
    {
        try
        {
            Customer? customer;
            using (MaxioWriteGuard.BeginScope())
            {
                var resp = await _client.Customers.CreateCustomer(body, ct: ct);
                customer = resp?.Customer;
            }

            return customer ?? throw new BillingException("The billing provider returned an empty customer response.", 502);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var payload))
            {
                var described = DescribeCustomerErrors(payload);
                throw new BillingException(
                    described is null
                        ? "The billing provider rejected the customer details."
                        : $"The billing provider rejected the customer details: {described}.",
                    422, ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRaw("create the customer", raw, ex);
            }

            throw new BillingException("The billing provider could not create the customer.", 502, ex);
        }
        catch (MaxioWriteRetryBlockedException ex)
        {
            throw UnknownWrite("create the customer", ex);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw UnknownWrite("create the customer", ex);
        }
        catch (JsonException ex)
        {
            throw Unprocessable("create the customer", ex);
        }
    }

    // ---- plans --------------------------------------------------------------------------

    private async Task<int> ResolveProductFamilyIdAsync(CancellationToken ct)
    {
        var handle = _settings.ProductFamilyHandle;
        if (string.IsNullOrWhiteSpace(handle))
        {
            throw new BillingException("No Maxio product family handle is configured (Maxio:ProductFamilyHandle).");
        }

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
            throw FromRaw("list product families", ex.Error, ex);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unreachable("list product families", ex);
        }
        catch (JsonException ex)
        {
            throw Unprocessable("list product families", ex);
        }

        var match = families.FirstOrDefault(f => string.Equals(f.ProductFamily?.Handle, handle, StringComparison.Ordinal));
        if (match?.ProductFamily?.Id is int id)
        {
            return id;
        }

        throw new BillingException($"The configured product family '{handle}' was not found in the billing system.", 404);
    }

    private async Task<IReadOnlyList<ProductResponse>> ListProductsAsync(int familyId, int page, int perPage, CancellationToken ct)
    {
        try
        {
            return await _client.ProductFamilies.ListProductsForProductFamily(
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
                perPage: perPage,
                ct: ct);
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out var msg))
            {
                throw new BillingException($"The billing provider could not list plans: {msg}", 404, ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRaw("list plans", raw, ex);
            }

            throw new BillingException("The billing provider could not list plans.", 502, ex);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unreachable("list plans", ex);
        }
        catch (JsonException ex)
        {
            throw Unprocessable("list plans", ex);
        }
    }

    // ---- subscriptions ------------------------------------------------------------------

    private async Task<CustomerSubscription> CreateSubscriptionRawAsync(CreateSubscriptionRequest body, CancellationToken ct)
    {
        try
        {
            Subscription? sub;
            using (MaxioWriteGuard.BeginScope())
            {
                var resp = await _client.Subscriptions.CreateSubscription(body, ct: ct);
                sub = resp?.Subscription;
            }

            return Map(sub ?? throw new BillingException("The billing provider returned an empty subscription response.", 502));
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var payload))
            {
                var described = payload.Errors is { Count: > 0 } ? string.Join("; ", payload.Errors) : null;
                throw new BillingException(
                    described is null
                        ? "The billing provider rejected the subscription."
                        : $"The billing provider rejected the subscription: {described}.",
                    422, ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRaw("create the subscription", raw, ex);
            }

            throw new BillingException("The billing provider could not create the subscription.", 502, ex);
        }
        catch (MaxioWriteRetryBlockedException ex)
        {
            throw UnknownWrite("create the subscription", ex);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw UnknownWrite("create the subscription", ex);
        }
        catch (JsonException ex)
        {
            throw Unprocessable("create the subscription", ex);
        }
    }

    private async Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsForCustomerAsync(int customerId, CancellationToken ct)
    {
        IReadOnlyList<SubscriptionResponse> subs;
        try
        {
            subs = await _client.Customers.ListCustomerSubscriptions(customerId, ct: ct);
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRaw("list subscriptions", ex.Error, ex);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unreachable("list subscriptions", ex);
        }
        catch (JsonException ex)
        {
            throw Unprocessable("list subscriptions", ex);
        }

        var result = new List<CustomerSubscription>(subs.Count);
        foreach (var s in subs)
        {
            if (s.Subscription is Subscription sub)
            {
                result.Add(Map(sub));
            }
        }

        return result;
    }

    // ---- mapping & helpers --------------------------------------------------------------

    private static CustomerSubscription Map(Subscription sub) => new(
        sub.Id ?? 0,
        sub.Product?.Handle,
        sub.Product?.Name,
        sub.Product?.Id,
        sub.ProductPriceInCents,
        sub.State?.Value ?? "unknown",
        sub.CurrentPeriodEndsAt,
        sub.NextAssessmentAt);

    private static bool IsLive(string state) => !TerminalStates.Contains(state);

    private static (string First, string Last) DeriveName(SubscribeRequest req)
    {
        var first = req.FirstName;
        if (string.IsNullOrWhiteSpace(first))
        {
            var at = req.Email.IndexOf('@');
            first = at > 0 ? req.Email[..at] : req.Email;
            if (string.IsNullOrWhiteSpace(first))
            {
                first = "eShop";
            }
        }

        var last = string.IsNullOrWhiteSpace(req.LastName) ? "eShopOnWeb Shopper" : req.LastName!;
        return (first!, last);
    }

    private static string? DescribeCustomerErrors(CustomerErrorResponse1 payload)
    {
        var errors = payload.Errors;
        if (errors is null)
        {
            return null;
        }

        var parts = new List<string>();
        if (errors.PerPage is { Count: > 0 } perPage)
        {
            parts.AddRange(perPage);
        }

        if (errors.PricePoint is { Count: > 0 } pricePoint)
        {
            parts.AddRange(pricePoint);
        }

        return parts.Count > 0 ? string.Join("; ", parts) : null;
    }

    private static BillingException FromRaw(string operation, RawError raw, Exception inner) =>
        new($"The billing provider returned status {(int)raw.StatusCode} while attempting to {operation}.", (int)raw.StatusCode, inner);

    private static BillingException Unreachable(string operation, Exception inner) =>
        new($"The billing provider is currently unreachable while attempting to {operation}.", 503, inner);

    private static BillingException UnknownWrite(string operation, Exception inner) =>
        new($"The billing provider did not confirm the request to {operation}; its outcome is unknown.", 502, inner, outcomeUnknown: true);

    private static BillingException Unprocessable(string operation, Exception inner) =>
        new($"The billing provider returned a response that could not be processed while attempting to {operation}.", 502, inner);
}
