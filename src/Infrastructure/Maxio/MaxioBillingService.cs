using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Maxio Advanced Billing implementation of <see cref="IMaxioBillingService"/>. Owns the Maxio SDK
/// client, maps SDK models onto the application's plain read models, and translates every SDK failure
/// into a <see cref="MaxioBillingException"/> carrying a caller-safe message and HTTP status.
/// </summary>
public class MaxioBillingService : IMaxioBillingService
{
    // Serialize subscribe attempts per shopper so a double-click cannot race two customer/subscription
    // creates. Static: shared across the scoped service instances within a single process.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();

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

    public async Task<IReadOnlyList<SubscriptionPlan>> GetSubscriptionPlansAsync(CancellationToken cancellationToken = default)
    {
        var familyHandle = _settings.ProductFamilyHandle;
        if (string.IsNullOrWhiteSpace(familyHandle))
            throw new MaxioBillingException("Maxio product family handle is not configured.", 500);

        try
        {
            // A product family may be addressed by numeric id or the "handle:<handle>" form.
            var products = await _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: $"handle:{familyHandle}",
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

            return products
                .Where(p => p.Product.ArchivedAt is null)
                .Select(p => MapPlan(p.Product))
                .OrderBy(p => p.PriceInCents)
                .ToList();
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out var message))
                throw new MaxioBillingException($"Could not list subscription plans: {message}", 404);
            if (ex.Error.TryGetRawError(out RawError raw))
                throw ToBillingException(raw, "Could not list subscription plans.");
            throw new MaxioBillingException("Could not list subscription plans.", 502, ex);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (IsTransport(ex)) { throw Unreachable(ex); }
        catch (JsonException ex) { throw Unprocessable(ex); }
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.UserReference))
            throw new MaxioBillingException("A user reference is required to subscribe.", 400);
        if (string.IsNullOrWhiteSpace(request.Email))
            throw new MaxioBillingException("An email is required to subscribe.", 400);

        var gate = Locks.GetOrAdd(request.UserReference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var planHandle = await ResolvePlanHandleAsync(request.PlanHandle, cancellationToken);

            var customer = await FindOrCreateCustomerAsync(request, cancellationToken);
            var customerId = customer.Id
                ?? throw new MaxioBillingException("The billing system returned a customer without an id.", 502);

            // Idempotency: if the shopper already has an active subscription to this plan (e.g. a
            // double-click landed a second request), return it instead of creating a duplicate.
            var existing = await ListSubscriptionsAsync(customerId, cancellationToken);
            var duplicate = existing.FirstOrDefault(s =>
                string.Equals(s.PlanHandle, planHandle, StringComparison.OrdinalIgnoreCase) && IsActiveLike(s.State));
            if (duplicate is not null)
            {
                _logger.LogInformation(
                    "Reusing existing Maxio subscription {SubscriptionId} for {Reference} on plan {Plan}.",
                    duplicate.SubscriptionId, request.UserReference, planHandle);
                return new SubscribeResult(duplicate, alreadyExisted: true);
            }

            var created = await CreateSubscriptionAsync(planHandle, customerId, cancellationToken);
            _logger.LogInformation(
                "Created Maxio subscription {SubscriptionId} for {Reference} on plan {Plan}.",
                created.SubscriptionId, request.UserReference, planHandle);
            return new SubscribeResult(created, alreadyExisted: false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsForUserAsync(
        string userReference, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userReference))
            throw new MaxioBillingException("A user reference is required.", 400);

        var customer = await FindCustomerByReferenceAsync(userReference, cancellationToken);
        if (customer?.Id is null)
            return Array.Empty<CustomerSubscription>();

        return await ListSubscriptionsAsync(customer.Id.Value, cancellationToken);
    }

    // --- customer -----------------------------------------------------------------------------

    private async Task<Customer> FindOrCreateCustomerAsync(SubscribeRequest request, CancellationToken ct)
    {
        var existing = await FindCustomerByReferenceAsync(request.UserReference, ct);
        if (existing is not null) return existing;

        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                Reference = request.UserReference,
                Email = request.Email,
                FirstName = ResolveFirstName(request),
                LastName = ResolveLastName(request)
            }
        };

        try
        {
            var created = await _client.Customers.CreateCustomer(body, ct);
            return created.Customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // Per the SDK contract, the typed 422 payload for CreateCustomer is unreliable — read the
            // raw error instead. A 422 here is most likely a duplicate reference from a concurrent
            // create (double-click); re-read by reference and use the customer that won the race.
            if (ex.Error.TryGetRawError(out RawError raw))
            {
                if ((int)raw.StatusCode == 422)
                {
                    var reread = await FindCustomerByReferenceAsync(request.UserReference, ct);
                    if (reread is not null) return reread;
                }
                throw ToBillingException(raw, "Could not create the billing customer.");
            }
            throw new MaxioBillingException("Could not create the billing customer.", 502, ex);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (IsTransport(ex)) { throw Unreachable(ex); }
        catch (JsonException ex) { throw Unprocessable(ex); }
    }

    private async Task<Customer?> FindCustomerByReferenceAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference, ct);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when ((int)ex.Error.StatusCode == 404)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw ToBillingException(ex.Error, "Could not look up the billing customer.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (IsTransport(ex)) { throw Unreachable(ex); }
        catch (JsonException ex) { throw Unprocessable(ex); }
    }

    // --- subscriptions ------------------------------------------------------------------------

    private async Task<CustomerSubscription> CreateSubscriptionAsync(string planHandle, int customerId, CancellationToken ct)
    {
        var body = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = planHandle,
                CustomerId = customerId,
                // Card-free / invoiced signup: no payment profile is attached.
                PaymentCollectionMethod = ParseCollectionMethod(_settings.PaymentCollectionMethod)
            }
        };

        try
        {
            var response = await _client.Subscriptions.CreateSubscription(body, ct);
            if (response.Subscription is null)
                throw new MaxioBillingException("The billing system did not return a subscription.", 502);
            return MapSubscription(response.Subscription, customerId);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            // CreateSubscription's typed 422 payload (a flat message list) is trustworthy.
            if (ex.Error.TryGetErrorListResponse1(out var errorList) && errorList?.Errors is { Count: > 0 })
            {
                var message = string.Join("; ", errorList.Errors);
                throw new MaxioBillingException($"The subscription could not be created: {message}", 422);
            }
            if (ex.Error.TryGetRawError(out RawError raw))
                throw ToBillingException(raw, "The subscription could not be created.");
            throw new MaxioBillingException("The subscription could not be created.", 502, ex);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (IsTransport(ex)) { throw Unreachable(ex); }
        catch (JsonException ex) { throw Unprocessable(ex); }
    }

    private async Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(int customerId, CancellationToken ct)
    {
        try
        {
            var subscriptions = await _client.Customers.ListCustomerSubscriptions(customerId, ct);
            return subscriptions
                .Where(s => s.Subscription is not null)
                .Select(s => MapSubscription(s.Subscription!, customerId))
                .ToList();
        }
        catch (SdkException<RawError> ex) when ((int)ex.Error.StatusCode == 404)
        {
            return Array.Empty<CustomerSubscription>();
        }
        catch (SdkException<RawError> ex)
        {
            throw ToBillingException(ex.Error, "Could not list the customer's subscriptions.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (IsTransport(ex)) { throw Unreachable(ex); }
        catch (JsonException ex) { throw Unprocessable(ex); }
    }

    private async Task<string> ResolvePlanHandleAsync(string? requested, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(requested)) return requested.Trim();
        if (!string.IsNullOrWhiteSpace(_settings.DefaultPlanHandle)) return _settings.DefaultPlanHandle.Trim();

        // No plan named and no configured default: fall back to the highest-priced active plan in the
        // family (the flagship "default subscribe target").
        var plans = await GetSubscriptionPlansAsync(ct);
        var target = plans.OrderByDescending(p => p.PriceInCents).FirstOrDefault();
        if (target is null)
            throw new MaxioBillingException("No subscription plans are available to subscribe to.", 502);
        return target.Handle;
    }

    // --- mapping ------------------------------------------------------------------------------

    private SubscriptionPlan MapPlan(Product product) => new()
    {
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? string.Empty,
        Description = product.Description,
        PriceInCents = product.PriceInCents ?? 0,
        Currency = "USD",
        Interval = product.Interval ?? 0,
        IntervalUnit = product.IntervalUnit?.Value ?? string.Empty,
        ProductFamilyHandle = _settings.ProductFamilyHandle
    };

    private static CustomerSubscription MapSubscription(Subscription subscription, int customerId) => new()
    {
        SubscriptionId = subscription.Id ?? 0,
        State = subscription.State?.Value ?? "unknown",
        PlanHandle = subscription.Product?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? string.Empty,
        PriceInCents = subscription.ProductPriceInCents
            ?? subscription.CurrentBillingAmountInCents
            ?? subscription.Product?.PriceInCents
            ?? 0,
        Currency = "USD",
        NextBillingAt = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt,
        CreatedAt = subscription.CurrentPeriodStartedAt,
        CustomerId = subscription.Customer?.Id ?? customerId
    };

    // --- helpers ------------------------------------------------------------------------------

    private static string ResolveFirstName(SubscribeRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.FirstName)) return request.FirstName.Trim();
        var email = request.Email;
        var at = email.IndexOf('@');
        var local = at > 0 ? email[..at] : email;
        return string.IsNullOrWhiteSpace(local) ? "eShop" : local;
    }

    private static string ResolveLastName(SubscribeRequest request)
        => string.IsNullOrWhiteSpace(request.LastName) ? "Shopper" : request.LastName.Trim();

    private static CollectionMethod ParseCollectionMethod(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "remittance" => CollectionMethod.Remittance,
            "automatic" => CollectionMethod.Automatic,
            "prepaid" => CollectionMethod.Prepaid,
            _ => CollectionMethod.Invoice
        };

    // Anything not in a terminal state counts as an existing subscription for idempotency.
    private static bool IsActiveLike(string state) =>
        state is not ("canceled" or "expired" or "failed_to_create");

    private static bool IsTransport(Exception ex) => ex is HttpRequestException or TaskCanceledException;

    private static MaxioBillingException Unreachable(Exception ex) =>
        new("The billing system is currently unreachable. Please try again.", 502, ex);

    private static MaxioBillingException Unprocessable(Exception ex) =>
        new("The billing system returned a response that could not be processed.", 502, ex);

    /// <summary>
    /// Translate a <see cref="RawError"/> into a caller-facing exception: a provider 4xx (which the
    /// caller can act on) passes through as that same client status; anything else is surfaced as 502.
    /// </summary>
    private static MaxioBillingException ToBillingException(RawError raw, string fallbackMessage)
    {
        var status = (int)raw.StatusCode;
        var isClientError = status is >= 400 and < 500;
        var surfaced = isClientError ? status : 502;

        var message = fallbackMessage;
        if (isClientError)
        {
            var body = SafeReadBody(raw);
            if (!string.IsNullOrWhiteSpace(body))
                message = $"{fallbackMessage} {body}";
        }

        return new MaxioBillingException(message, surfaced);
    }

    private static string? SafeReadBody(RawError raw)
    {
        try
        {
            return raw.ReadAsString();
        }
        catch
        {
            return null;
        }
    }
}
