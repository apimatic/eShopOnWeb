using System;
using System.Collections.Generic;
using System.Globalization;
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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using JsonException = System.Text.Json.JsonException;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Maxio Advanced Billing implementation of <see cref="ISubscriptionBillingService"/>.
/// Maxio is the system of record; this type is the single boundary that translates every
/// provider failure (API error, transport failure, unreadable body) into a
/// <see cref="SubscriptionBillingException"/> carrying a caller-safe message and status.
/// </summary>
public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    // A subscription in any of these states is treated as terminal (not "live"); anything
    // else — including unknown/future states — counts as an existing subscription so the
    // idempotency guard never accidentally double-creates.
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "trial_ended", "failed_to_create"
    };

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default)
    {
        var familyId = await ResolveProductFamilyIdAsync(cancellationToken);

        var plans = new List<SubscriptionPlan>();
        int page = 1;
        const int perPage = 200;

        while (true)
        {
            IReadOnlyList<ProductResponse> pageItems;
            try
            {
                pageItems = await _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: familyId,
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
                    ct: cancellationToken);
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetRawError(out var raw))
                    throw FromRawError(raw, "list the subscription plans");
                throw new SubscriptionBillingException("Unable to list the subscription plans.", 502, ex);
            }
            catch (SdkException<RawError> ex) { throw FromRawError(ex.Error, "list the subscription plans"); }
            catch (JsonException ex) { throw Unprocessable(ex, "list the subscription plans"); }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unreachable(ex, "list the subscription plans"); }

            foreach (var pr in pageItems)
            {
                var product = pr.Product;
                if (product is null)
                    continue;

                plans.Add(new SubscriptionPlan
                {
                    Handle = product.Handle ?? string.Empty,
                    Name = product.Name ?? string.Empty,
                    Description = product.Description,
                    PriceInCents = product.PriceInCents ?? 0,
                    Interval = product.Interval ?? 0,
                    IntervalUnit = product.IntervalUnit?.Value
                });
            }

            if (pageItems.Count < perPage)
                break;
            page++;
        }

        return plans;
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.UserReference))
            throw new SubscriptionBillingException("A user reference is required to subscribe.", 400);
        if (string.IsNullOrWhiteSpace(request.ProductHandle))
            throw new SubscriptionBillingException("A plan handle is required to subscribe.", 400);

        var customerId = await EnsureCustomerAsync(request, cancellationToken);

        // Idempotency guard: a live subscription to the same plan already exists → return it, don't duplicate.
        var existing = await FindLiveSubscriptionAsync(customerId, request.ProductHandle, cancellationToken);
        if (existing is not null)
            return new SubscribeResult(MapSubscription(existing), alreadyExisted: true);

        Subscription created;
        try
        {
            var body = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = request.ProductHandle,
                    CustomerId = customerId,
                    // Bill by invoice/remittance so no card capture is required. The default
                    // (Automatic) would try to auto-collect the first balance and 422 without a card.
                    PaymentCollectionMethod = ResolveCollectionMethod()
                }
            };

            var response = await _client.Subscriptions.CreateSubscription(body, cancellationToken);
            created = response.Subscription
                ?? throw Unprocessable(new InvalidOperationException("subscription missing from response"), "create the subscription");
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorList) && errorList?.Errors is { Count: > 0 } errors)
                throw new SubscriptionBillingException($"Unable to create the subscription: {string.Join("; ", errors)}", 422, ex);
            if (ex.Error.TryGetRawError(out var raw))
                throw FromRawError(raw, "create the subscription");
            throw new SubscriptionBillingException("Unable to create the subscription.", 502, ex);
        }
        catch (SdkException<RawError> ex) { throw FromRawError(ex.Error, "create the subscription"); }
        catch (JsonException ex) { throw Unprocessable(ex, "create the subscription"); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // A transport failure may have double-sent this POST. Reconcile against provider state
            // before deciding the outcome, rather than assuming nothing happened.
            var reconciled = await FindLiveSubscriptionAsync(customerId, request.ProductHandle, CancellationToken.None);
            if (reconciled is not null)
                return new SubscribeResult(MapSubscription(reconciled), alreadyExisted: true);
            throw Unreachable(ex, "create the subscription");
        }

        return new SubscribeResult(MapSubscription(created), alreadyExisted: false);
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsForUserAsync(string userReference, CancellationToken cancellationToken = default)
    {
        var customer = await TryReadCustomerByReferenceAsync(userReference, cancellationToken);
        if (customer?.Id is not int customerId)
            return Array.Empty<CustomerSubscription>();

        var subscriptions = await ListCustomerSubscriptionsAsync(customerId, cancellationToken);

        var result = new List<CustomerSubscription>(subscriptions.Count);
        foreach (var sr in subscriptions)
        {
            if (sr.Subscription is { } subscription)
                result.Add(MapSubscription(subscription));
        }
        return result;
    }

    // --- customer ---

    private async Task<int> EnsureCustomerAsync(SubscribeRequest request, CancellationToken ct)
    {
        var existing = await TryReadCustomerByReferenceAsync(request.UserReference, ct);
        if (existing?.Id is int existingId)
            return existingId;

        var (firstName, lastName) = DeriveName(request);
        try
        {
            var body = new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = request.Email,
                    Reference = request.UserReference
                }
            };

            var response = await _client.Customers.CreateCustomer(body, ct);
            if (response.Customer?.Id is int newId)
                return newId;

            throw Unprocessable(new InvalidOperationException("customer id missing from response"), "create the billing customer");
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // A concurrent request (double-click) may have already created the customer for this
            // reference — re-read before treating this as a failure.
            var raced = await TryReadCustomerByReferenceAsync(request.UserReference, ct);
            if (raced?.Id is int racedId)
                return racedId;

            throw new SubscriptionBillingException(BuildCustomerErrorMessage(ex.Error), 422, ex);
        }
        catch (SdkException<RawError> ex) { throw FromRawError(ex.Error, "create the billing customer"); }
        catch (JsonException ex)
        {
            // An unreadable create response might be a real success we couldn't parse — reconcile
            // rather than mapping a parse failure onto "customer absent".
            var raced = await TryReadCustomerByReferenceAsync(request.UserReference, ct);
            if (raced?.Id is int racedId)
                return racedId;
            throw Unprocessable(ex, "create the billing customer");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            var raced = await TryReadCustomerByReferenceAsync(request.UserReference, CancellationToken.None);
            if (raced?.Id is int racedId)
                return racedId;
            throw Unreachable(ex, "create the billing customer");
        }
    }

    private async Task<Customer?> TryReadCustomerByReferenceAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference, ct);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            // Confirmed 404 → customer does not exist yet. Any other status is a real failure.
            return null;
        }
        catch (SdkException<RawError> ex) { throw FromRawError(ex.Error, "look up the billing customer"); }
        catch (JsonException ex) { throw Unprocessable(ex, "look up the billing customer"); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unreachable(ex, "look up the billing customer"); }
    }

    private string BuildCustomerErrorMessage(CreateCustomerError error)
    {
        // The typed 422 body (CustomerErrorResponse1.Errors) only carries per_page/price_point
        // lists, so fall back to the raw body for a meaningful message.
        var parts = new List<string>();
        if (error.TryGetCustomerErrorResponse1(out var typed) && typed?.Errors is { } errors)
        {
            if (errors.PerPage is { } perPage) parts.AddRange(perPage);
            if (errors.PricePoint is { } pricePoint) parts.AddRange(pricePoint);
        }

        if (parts.Count > 0)
            return $"Unable to create the billing customer: {string.Join("; ", parts)}";

        if (error.TryGetRawError(out var raw))
        {
            var body = raw.ReadAsString();
            if (!string.IsNullOrWhiteSpace(body))
                return $"Unable to create the billing customer: {body}";
        }

        return "Unable to create the billing customer.";
    }

    // --- subscriptions ---

    private async Task<Subscription?> FindLiveSubscriptionAsync(int customerId, string productHandle, CancellationToken ct)
    {
        var subscriptions = await ListCustomerSubscriptionsAsync(customerId, ct);
        foreach (var sr in subscriptions)
        {
            var subscription = sr.Subscription;
            if (subscription is null)
                continue;
            if (!string.Equals(subscription.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase))
                continue;
            if (IsLive(subscription))
                return subscription;
        }
        return null;
    }

    private async Task<IReadOnlyList<SubscriptionResponse>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct)
    {
        try
        {
            return await _client.Customers.ListCustomerSubscriptions(customerId, ct);
        }
        catch (SdkException<RawError> ex) { throw FromRawError(ex.Error, "list the customer's subscriptions"); }
        catch (JsonException ex) { throw Unprocessable(ex, "list the customer's subscriptions"); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unreachable(ex, "list the customer's subscriptions"); }
    }

    private async Task<string> ResolveProductFamilyIdAsync(CancellationToken ct)
    {
        var handle = _settings.ProductFamilyHandle;
        if (string.IsNullOrWhiteSpace(handle))
            throw new SubscriptionBillingException("The Maxio product family handle is not configured.", 500);

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
        catch (SdkException<RawError> ex) { throw FromRawError(ex.Error, "list the product families"); }
        catch (JsonException ex) { throw Unprocessable(ex, "list the product families"); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unreachable(ex, "list the product families"); }

        foreach (var fr in families)
        {
            var family = fr.ProductFamily;
            if (family is not null
                && string.Equals(family.Handle, handle, StringComparison.OrdinalIgnoreCase)
                && family.Id is int id)
            {
                return id.ToString(CultureInfo.InvariantCulture);
            }
        }

        throw new SubscriptionBillingException($"The configured Maxio product family '{handle}' was not found.", 500);
    }

    private static bool IsLive(Subscription subscription)
    {
        var state = subscription.State?.Value;
        return state is null || !TerminalStates.Contains(state);
    }

    private static CustomerSubscription MapSubscription(Subscription subscription) => new()
    {
        Id = subscription.Id ?? 0,
        ProductHandle = subscription.Product?.Handle,
        ProductName = subscription.Product?.Name,
        State = subscription.State?.Value,
        PriceInCents = subscription.CurrentBillingAmountInCents ?? subscription.ProductPriceInCents,
        NextBillingDate = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
        CustomerReference = subscription.Reference
    };

    private CollectionMethod ResolveCollectionMethod()
    {
        var configured = _settings.PaymentCollectionMethod?.Trim().ToLowerInvariant();
        return configured switch
        {
            "automatic" => CollectionMethod.Automatic,
            "invoice" => CollectionMethod.Invoice,
            "prepaid" => CollectionMethod.Prepaid,
            _ => CollectionMethod.Remittance
        };
    }

    private static (string firstName, string lastName) DeriveName(SubscribeRequest request)
    {
        var firstName = string.IsNullOrWhiteSpace(request.FirstName)
            ? DeriveFirstNameFromEmail(request.Email)
            : request.FirstName!.Trim();
        var lastName = string.IsNullOrWhiteSpace(request.LastName)
            ? "eShop Customer"
            : request.LastName!.Trim();
        return (firstName, lastName);
    }

    private static string DeriveFirstNameFromEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return "eShop";
        var at = email.IndexOf('@');
        var local = at > 0 ? email[..at] : email;
        return string.IsNullOrWhiteSpace(local) ? "eShop" : local;
    }

    // --- failure translation (the single boundary) ---

    private SubscriptionBillingException FromRawError(RawError raw, string action)
    {
        var status = (int)raw.StatusCode;
        _logger.LogError("Maxio failed to {Action}: HTTP {Status} {Body}", action, status, SafeBody(raw));

        // Provider 4xx (the caller can act on it) surfaces as that same client status; everything
        // else has no meaningful client status and surfaces as 502.
        var clientStatus = status is >= 400 and < 500 ? status : 502;
        var message = status is >= 400 and < 500
            ? $"Unable to {action}. The billing provider rejected the request (HTTP {status})."
            : $"Unable to {action}. The billing provider returned an error.";
        return new SubscriptionBillingException(message, clientStatus);
    }

    private SubscriptionBillingException Unreachable(Exception ex, string action)
    {
        _logger.LogError(ex, "Maxio failed to {Action}: provider unreachable", action);
        return new SubscriptionBillingException($"Unable to {action}. The billing provider is currently unreachable.", 503, ex);
    }

    private SubscriptionBillingException Unprocessable(Exception ex, string action)
    {
        _logger.LogError(ex, "Maxio failed to {Action}: response could not be processed", action);
        return new SubscriptionBillingException($"Unable to {action}. The billing provider returned a response that could not be processed.", 502, ex);
    }

    private static string SafeBody(RawError raw)
    {
        try { return raw.ReadAsString(); }
        catch { return "<unreadable>"; }
    }
}
