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
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    // Live states per the Maxio SubscriptionState enum - a customer in one of these already has a
    // going subscription, so a repeated subscribe-to-the-same-plan call must not create another one.
    // Canceled/Expired/FailedToCreate (and any other state) do not block a new signup.
    private static readonly HashSet<string> LiveSubscriptionStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "trialing", "past_due", "suspended", "on_hold", "awaiting_signup", "soft_failure", "unpaid"
    };

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;

    public MaxioSubscriptionService(MaxioAdvancedBillingClient client, IOptions<MaxioOptions> options)
    {
        _client = client;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken ct = default)
    {
        try
        {
            var currency = await GetSiteCurrencyAsync(ct);
            var products = await ListFamilyProductsAsync(ct);

            return products
                .Select(p => p.Product)
                .Where(p => p is not null)
                .Select(p => new SubscriptionPlan
                {
                    Id = p!.Id,
                    Handle = p.Handle,
                    Name = p.Name,
                    PriceInCents = p.PriceInCents,
                    Currency = currency,
                    Interval = p.Interval,
                    IntervalUnit = p.IntervalUnit?.Value
                })
                .ToList();
        }
        catch (MaxioIntegrationException)
        {
            throw;
        }
        catch (Exception ex) when (IsTransportOrParseFailure(ex))
        {
            throw ToProviderUnavailable(ex);
        }
    }

    public async Task<CustomerSubscription> SubscribeAsync(
        string customerReference,
        string email,
        string firstName,
        string lastName,
        string planHandle,
        CancellationToken ct = default)
    {
        try
        {
            var customerId = await EnsureCustomerAsync(customerReference, email, firstName, lastName, ct);
            var currency = await GetSiteCurrencyAsync(ct);

            var existing = await FindLiveSubscriptionAsync(customerId, planHandle, currency, ct);
            if (existing is not null)
            {
                return existing;
            }

            return await CreateSubscriptionAsync(customerId, planHandle, currency, ct);
        }
        catch (MaxioIntegrationException)
        {
            throw;
        }
        catch (Exception ex) when (IsTransportOrParseFailure(ex))
        {
            throw ToProviderUnavailable(ex);
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListMySubscriptionsAsync(
        string customerReference,
        CancellationToken ct = default)
    {
        try
        {
            var customerId = await TryReadCustomerIdAsync(customerReference, ct);
            if (customerId is null)
            {
                return Array.Empty<CustomerSubscription>();
            }

            var currency = await GetSiteCurrencyAsync(ct);
            return await ListCustomerSubscriptionsAsync(customerId.Value, currency, ct);
        }
        catch (MaxioIntegrationException)
        {
            throw;
        }
        catch (Exception ex) when (IsTransportOrParseFailure(ex))
        {
            throw ToProviderUnavailable(ex);
        }
    }

    private async Task<List<ProductResponse>> ListFamilyProductsAsync(CancellationToken ct)
    {
        var productFamilyId = $"handle:{_options.ProductFamilyHandle}";
        var results = new List<ProductResponse>();
        var page = 1;
        const int perPage = 50;

        try
        {
            while (true)
            {
                var pageResults = await _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: productFamilyId,
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

                results.AddRange(pageResults);
                if (pageResults.Count < perPage)
                {
                    break;
                }

                page++;
            }

            return results;
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out var notFoundMessage))
            {
                throw new MaxioIntegrationException(HttpStatusCode.NotFound,
                    $"Maxio product family '{_options.ProductFamilyHandle}' was not found: {notFoundMessage}", ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new MaxioIntegrationException(MapStatus(raw.StatusCode),
                    $"Unable to list subscription plans: {raw.ReadAsString()}", ex);
            }

            throw new MaxioIntegrationException(HttpStatusCode.BadGateway, "Unable to list subscription plans.", ex);
        }
    }

    private async Task<string> GetSiteCurrencyAsync(CancellationToken ct)
    {
        try
        {
            var response = await _client.Sites.ReadSite(ct: ct);
            return response.Site.Currency ?? "USD";
        }
        catch (SdkException<RawError> ex)
        {
            throw new MaxioIntegrationException(MapStatus(ex.Error.StatusCode),
                $"Unable to read Maxio site information: {ex.Error.ReadAsString()}", ex);
        }
    }

    /// <summary>
    /// Idempotent find-or-create: Maxio has no atomic upsert-by-reference. Look the customer up
    /// first; if a concurrent caller created it between the lookup and our own create attempt (a
    /// 422 from CreateCustomer), re-look-up once more instead of failing - this closes the
    /// double-click race without ever creating two customers for the same reference.
    /// </summary>
    private async Task<int> EnsureCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken ct)
    {
        var existingId = await TryReadCustomerIdAsync(reference, ct);
        if (existingId is int id)
        {
            return id;
        }

        try
        {
            var created = await _client.Customers.CreateCustomer(
                body: new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = firstName,
                        LastName = lastName,
                        Email = email,
                        Reference = reference
                    }
                },
                ct: ct);

            if (created.Customer?.Id is int newId)
            {
                return newId;
            }

            throw new MaxioIntegrationException(HttpStatusCode.BadGateway,
                "Maxio did not return a customer id after creation.");
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // A concurrent duplicate create can race us here - re-check before treating this as a
            // hard failure (see EnsureCustomerAsync's doc comment).
            var raceId = await TryReadCustomerIdAsync(reference, ct);
            if (raceId is int resolvedId)
            {
                return resolvedId;
            }

            throw new MaxioIntegrationException(HttpStatusCode.UnprocessableEntity,
                $"Unable to create Maxio customer for reference '{reference}': {DescribeCreateCustomerError(ex.Error)}", ex);
        }
    }

    private async Task<int?> TryReadCustomerIdAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference: reference, ct: ct);
            return response.Customer?.Id;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw new MaxioIntegrationException(MapStatus(ex.Error.StatusCode),
                $"Maxio customer lookup failed for reference '{reference}': {ex.Error.ReadAsString()}", ex);
        }
    }

    /// <summary>
    /// App-level dedup for subscription creation - CreateSubscription has no idempotency key and no
    /// documented rejection of a second subscription to the same product, so a repeat POST for a
    /// customer that already has a live subscription to this plan must return that one instead of
    /// enrolling them twice.
    /// </summary>
    private async Task<CustomerSubscription?> FindLiveSubscriptionAsync(int customerId, string planHandle, string currency, CancellationToken ct)
    {
        var subscriptions = await ListCustomerSubscriptionsAsync(customerId, currency, ct);
        return subscriptions.FirstOrDefault(s =>
            string.Equals(s.ProductHandle, planHandle, StringComparison.OrdinalIgnoreCase) &&
            s.State is not null && LiveSubscriptionStates.Contains(s.State));
    }

    private async Task<List<CustomerSubscription>> ListCustomerSubscriptionsAsync(int customerId, string currency, CancellationToken ct)
    {
        try
        {
            var subscriptions = await _client.Customers.ListCustomerSubscriptions(customerId: customerId, ct: ct);
            return subscriptions
                .Select(s => s.Subscription)
                .Where(s => s is not null)
                .Select(s => MapSubscription(s!, currency))
                .ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw new MaxioIntegrationException(MapStatus(ex.Error.StatusCode),
                $"Unable to list Maxio subscriptions for customer {customerId}: {ex.Error.ReadAsString()}", ex);
        }
    }

    private async Task<CustomerSubscription> CreateSubscriptionAsync(int customerId, string planHandle, string currency, CancellationToken ct)
    {
        try
        {
            var created = await _client.Subscriptions.CreateSubscription(
                body: new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = planHandle,
                        CustomerId = customerId
                    }
                },
                ct: ct);

            if (created.Subscription is null)
            {
                throw new MaxioIntegrationException(HttpStatusCode.BadGateway,
                    "Maxio did not return a subscription after creation.");
            }

            return MapSubscription(created.Subscription, currency);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                throw new MaxioIntegrationException(HttpStatusCode.UnprocessableEntity,
                    $"Unable to subscribe to plan '{planHandle}': {string.Join("; ", errorList.Errors)}", ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new MaxioIntegrationException(MapStatus(raw.StatusCode),
                    $"Unable to subscribe to plan '{planHandle}': {raw.ReadAsString()}", ex);
            }

            throw new MaxioIntegrationException(HttpStatusCode.BadGateway,
                $"Unable to subscribe to plan '{planHandle}'.", ex);
        }
    }

    private static CustomerSubscription MapSubscription(Subscription subscription, string currency) => new()
    {
        Id = subscription.Id,
        State = subscription.State?.Value,
        NextAssessmentAt = subscription.NextAssessmentAt,
        ProductName = subscription.Product?.Name,
        ProductHandle = subscription.Product?.Handle,
        PriceInCents = subscription.Product?.PriceInCents,
        Currency = currency
    };

    private static string DescribeCreateCustomerError(CreateCustomerError error)
    {
        // CustomerErrorResponse1 is a generic error record reused across unrelated operations - it
        // carries per_page/price_point fields, nothing about a customer or reference conflict - so it
        // is checked only to route past it, never parsed for content.
        if (error.TryGetCustomerErrorResponse1(out _))
        {
            return "Maxio rejected the customer create request (422).";
        }

        if (error.TryGetRawError(out var raw))
        {
            return raw.ReadAsString();
        }

        return "Maxio rejected the customer create request.";
    }

    private static bool IsTransportOrParseFailure(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or JsonException;

    private static MaxioIntegrationException ToProviderUnavailable(Exception ex) =>
        new(HttpStatusCode.BadGateway, "The billing provider is currently unavailable.", ex);

    private static HttpStatusCode MapStatus(HttpStatusCode providerStatus) =>
        (int)providerStatus is >= 400 and < 500 ? providerStatus : HttpStatusCode.BadGateway;
}
