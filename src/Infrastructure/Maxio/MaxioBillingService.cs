using System;
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
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// <see cref="ISubscriptionBillingService"/> backed by Maxio Advanced Billing. All provider
/// failures — typed errors, raw errors, transport faults and unreadable bodies — are translated
/// into <see cref="BillingException"/> at this boundary so callers see one failure type.
/// </summary>
public class MaxioBillingService : ISubscriptionBillingService
{
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

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await ListFamilyPlansAsync(cancellationToken);
        }
        catch (BillingException) { throw; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unavailable(ex); }
        catch (JsonException ex) { throw Unprocessable(ex); }
    }

    public async Task<SubscribeResult> SubscribeAsync(string userName, string planHandle, CancellationToken cancellationToken = default)
    {
        try
        {
            // 1. Validate the requested plan against the configured family — a shopper may only
            //    subscribe to one of eShop's own plans, and this yields a clean 404 for a bad handle.
            var plans = await ListFamilyPlansAsync(cancellationToken);
            var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
            if (plan is null)
            {
                throw new BillingException($"Plan '{planHandle}' is not an available subscription plan.", 404);
            }

            // 2. Ensure a single Maxio customer exists for this eShop user (idempotent).
            var customerId = await EnsureCustomerIdAsync(userName, cancellationToken);

            // 3. Dedup: if a live subscription to this plan already exists, return it rather than
            //    creating a second one (double-click / retry safe; CreateSubscription has no key).
            var existing = await FindLiveSubscriptionAsync(customerId, plan.Handle, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation("Reusing existing live Maxio subscription {SubscriptionId} on plan {Plan} for customer {CustomerId}",
                    existing.Id, plan.Handle, customerId);
                return new SubscribeResult(existing, AlreadyActive: true);
            }

            // 4. Create the subscription.
            var created = await CreateSubscriptionAsync(customerId, plan.Handle, cancellationToken);
            _logger.LogInformation("Created Maxio subscription {SubscriptionId} on plan {Plan} for customer {CustomerId}",
                created.Id, plan.Handle, customerId);
            return new SubscribeResult(created, AlreadyActive: false);
        }
        catch (BillingException) { throw; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unavailable(ex); }
        catch (JsonException ex) { throw Unprocessable(ex); }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(string userName, CancellationToken cancellationToken = default)
    {
        try
        {
            var customerId = await TryGetCustomerIdByReferenceAsync(CustomerReference(userName), cancellationToken);
            if (customerId is null)
            {
                return Array.Empty<CustomerSubscription>();
            }

            IReadOnlyList<SubscriptionResponse> subscriptions;
            try
            {
                subscriptions = await _client.Customers.ListCustomerSubscriptions(customerId.Value, ct: cancellationToken);
            }
            catch (SdkException<RawError> ex)
            {
                throw FromRaw(ex.Error, ex, "list customer subscriptions");
            }

            return subscriptions
                .Select(s => s.Subscription)
                .Where(s => s is not null)
                .Select(s => ToSubscription(s!))
                .ToList();
        }
        catch (BillingException) { throw; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unavailable(ex); }
        catch (JsonException ex) { throw Unprocessable(ex); }
    }

    // --- SDK call wrappers (each translates its own operation's errors) ---

    private async Task<IReadOnlyList<SubscriptionPlan>> ListFamilyPlansAsync(CancellationToken ct)
    {
        IReadOnlyList<ProductResponse> products;
        try
        {
            products = await _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: $"handle:{_settings.ProductFamilyHandle}",
                dateField: null,
                filter: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                includeArchived: false,
                include: null,
                ct: ct);
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out var _))
            {
                // 404 — the configured product family handle does not exist. This is our
                // (deployment) fault, not the caller's; surface it as such.
                _logger.LogError(ex, "Configured Maxio product family '{Family}' was not found.", _settings.ProductFamilyHandle);
                throw new BillingException(
                    $"The configured billing product family '{_settings.ProductFamilyHandle}' was not found.", null, ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRaw(raw, ex, "list products for product family");
            }

            throw new BillingException("The billing provider returned an unrecognised error.", null, ex);
        }

        return products
            .Select(p => p.Product)
            .Where(p => p.ArchivedAt is null)
            .Select(ToPlan)
            .ToList();
    }

    private async Task<int> EnsureCustomerIdAsync(string userName, CancellationToken ct)
    {
        var reference = CustomerReference(userName);

        var existing = await TryGetCustomerIdByReferenceAsync(reference, ct);
        if (existing is not null)
        {
            return existing.Value;
        }

        try
        {
            var response = await _client.Customers.CreateCustomer(new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = userName,
                    LastName = "eShopOnWeb",
                    Email = userName,
                    Reference = reference,
                },
            }, ct: ct);

            var id = response.Customer.Id
                ?? throw new BillingException("The billing provider did not return a customer id.");
            _logger.LogInformation("Created Maxio customer {CustomerId} for reference {Reference}", id, reference);
            return id;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // A 422 here can be a reference conflict from a concurrent create (double-click) —
            // re-read by reference before treating it as a genuine validation failure.
            var raced = await TryGetCustomerIdByReferenceAsync(reference, ct);
            if (raced is not null)
            {
                return raced.Value;
            }

            if (ex.Error.TryGetCustomerErrorResponse1(out var body))
            {
                throw new BillingException($"The billing provider rejected the customer: {DescribeCustomerErrors(body)}", 422, ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRaw(raw, ex, "create customer");
            }

            throw new BillingException("The billing provider returned an unrecognised error creating the customer.", null, ex);
        }
    }

    private async Task<int?> TryGetCustomerIdByReferenceAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference, ct: ct);
            return response.Customer.Id;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            // No customer with this reference yet.
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRaw(ex.Error, ex, "read customer by reference");
        }
    }

    private async Task<CustomerSubscription?> FindLiveSubscriptionAsync(int customerId, string planHandle, CancellationToken ct)
    {
        IReadOnlyList<SubscriptionResponse> subscriptions;
        try
        {
            subscriptions = await _client.Customers.ListCustomerSubscriptions(customerId, ct: ct);
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRaw(ex.Error, ex, "list customer subscriptions");
        }

        var match = subscriptions
            .Select(s => s.Subscription)
            .FirstOrDefault(s => s is not null
                && string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase)
                && IsLive(s.State?.Value));

        return match is null ? null : ToSubscription(match);
    }

    private async Task<CustomerSubscription> CreateSubscriptionAsync(int customerId, string planHandle, CancellationToken ct)
    {
        try
        {
            var response = await _client.Subscriptions.CreateSubscription(new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = planHandle,
                    CustomerId = customerId,
                },
            }, ct: ct);

            var subscription = response.Subscription
                ?? throw new BillingException("The billing provider did not return a subscription.");
            return ToSubscription(subscription);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var body))
            {
                throw new BillingException($"The billing provider rejected the subscription: {string.Join("; ", body.Errors)}", 422, ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRaw(raw, ex, "create subscription");
            }

            throw new BillingException("The billing provider returned an unrecognised error creating the subscription.", null, ex);
        }
    }

    // --- Mapping ---

    private static SubscriptionPlan ToPlan(Product p) => new(
        Handle: p.Handle ?? string.Empty,
        Name: p.Name ?? string.Empty,
        Description: p.Description,
        PriceInCents: p.PriceInCents ?? 0,
        Interval: p.Interval ?? 0,
        IntervalUnit: p.IntervalUnit?.Value);

    private static CustomerSubscription ToSubscription(Subscription s) => new(
        Id: s.Id ?? 0,
        PlanHandle: s.Product?.Handle,
        PlanName: s.Product?.Name,
        State: s.State?.Value ?? "unknown",
        PriceInCents: s.ProductPriceInCents,
        CurrentPeriodEndsAt: s.CurrentPeriodEndsAt,
        NextBillingDate: s.NextAssessmentAt ?? s.CurrentPeriodEndsAt);

    // --- Helpers ---

    private static string CustomerReference(string userName) => $"eshoponweb:{userName}";

    /// <summary>Subscription states in which a subscription still counts as live (existing).</summary>
    private static bool IsLive(string? stateValue) => stateValue is not null
        && stateValue is not ("canceled" or "expired" or "failed_to_create" or "trial_ended" or "unpaid");

    private static string DescribeCustomerErrors(CustomerErrorResponse1 body)
    {
        if (body.Errors is { } errors && errors.TryGetListOfString(out var messages) && messages.Count > 0)
        {
            return string.Join("; ", messages);
        }

        return "validation failed";
    }

    private static BillingException Unavailable(Exception ex) =>
        new("The billing provider is currently unavailable. Please try again later.", null, ex);

    private static BillingException Unprocessable(Exception ex) =>
        new("The billing provider returned a response that could not be processed.", null, ex);

    private BillingException FromRaw(RawError raw, Exception ex, string context)
    {
        var status = (int)raw.StatusCode;
        _logger.LogWarning(ex, "Maxio call '{Context}' failed: HTTP {Status} {Body}", context, status, SafeReadBody(raw));
        return new BillingException($"The billing provider rejected the request (HTTP {status}).", status, ex);
    }

    private static string SafeReadBody(RawError raw)
    {
        try
        {
            return raw.ReadAsString();
        }
        catch
        {
            return "<unreadable body>";
        }
    }
}
