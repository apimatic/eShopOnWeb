using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public class MaxioBillingService : IMaxioBillingService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);
    private const string ProductFamilyIdCacheKeyPrefix = "Maxio:ProductFamilyId:";
    private const string SiteCurrencyCacheKey = "Maxio:SiteCurrency";
    private const int PlansPerPage = 20;

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly IMemoryCache _cache;

    public MaxioBillingService(MaxioAdvancedBillingClient client, IOptions<MaxioOptions> options, IMemoryCache cache)
    {
        _client = client;
        _options = options.Value;
        _cache = cache;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken ct)
    {
        var familyIdTask = ResolveProductFamilyIdAsync(ct);
        var currencyTask = ResolveDisplayCurrencyAsync(ct);
        await Task.WhenAll(familyIdTask, currencyTask);
        var familyId = familyIdTask.Result;
        var currency = currencyTask.Result;

        var plans = new List<SubscriptionPlan>();
        var page = 1;

        while (true)
        {
            IReadOnlyList<ProductResponse> pageResult;
            try
            {
                pageResult = await _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: familyId.ToString(),
                    dateField: null,
                    filter: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    includeArchived: false,
                    include: null,
                    page: page,
                    perPage: PlansPerPage,
                    ct: ct);
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetString(out var notFoundMessage))
                    throw new MaxioBillingException($"Unable to list plans: {notFoundMessage}", 404, ex);
                if (ex.Error.TryGetRawError(out var raw))
                    throw new MaxioBillingException($"Unable to list plans: {raw.ReadAsString()}", 502, ex);
                throw new MaxioBillingException("Unable to list plans.", 502, ex);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                throw new MaxioBillingException("Billing provider unreachable.", 502, ex);
            }
            catch (JsonException ex)
            {
                throw new MaxioBillingException("The billing provider returned a response that could not be processed.", 502, ex);
            }

            foreach (var item in pageResult)
            {
                if (item.Product is not null)
                {
                    plans.Add(MapPlan(item.Product, currency));
                }
            }

            if (pageResult.Count < PlansPerPage)
            {
                break;
            }

            page++;
        }

        return plans;
    }

    public async Task<UserSubscription> SubscribeAsync(string userId, string userEmail, string planHandle, CancellationToken ct)
    {
        var customer = await EnsureCustomerAsync(userId, userEmail, ct);
        var customerId = customer.Id!.Value;

        var existing = await FindSubscriptionAsync(customerId, planHandle, ct);
        if (existing is not null)
        {
            return MapSubscription(existing);
        }

        var createRequest = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = planHandle,
                CustomerId = customerId,
                // These plans are configured with no required payment method (no trial, no setup
                // fee, no card capture) -- Remittance tells Maxio to bill outside of an automatic
                // card charge instead of defaulting to the site's "automatic" collection method,
                // which would otherwise require a card on file.
                PaymentCollectionMethod = CollectionMethod.Remittance
            }
        };

        try
        {
            var response = await _client.Subscriptions.CreateSubscription(createRequest, ct);
            if (response.Subscription is null)
            {
                throw new MaxioBillingException("Billing provider returned an empty subscription.", 502);
            }

            return MapSubscription(response.Subscription);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errors))
            {
                throw new MaxioBillingException($"Unable to create subscription: {string.Join("; ", errors.Errors)}", 400, ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new MaxioBillingException($"Unable to create subscription: {raw.ReadAsString()}", 502, ex);
            }
            throw new MaxioBillingException("Unable to create subscription.", 502, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // The write's outcome is unknown, not failed -- reconcile against provider state before reporting an error.
            var reconciled = await FindSubscriptionAsync(customerId, planHandle, ct);
            if (reconciled is not null)
            {
                return MapSubscription(reconciled);
            }
            throw new MaxioBillingException("Billing provider unreachable.", 502, ex);
        }
        catch (JsonException ex)
        {
            var reconciled = await FindSubscriptionAsync(customerId, planHandle, ct);
            if (reconciled is not null)
            {
                return MapSubscription(reconciled);
            }
            throw new MaxioBillingException("The billing provider returned a response that could not be processed.", 502, ex);
        }
    }

    public async Task<IReadOnlyList<UserSubscription>> ListMySubscriptionsAsync(string userId, CancellationToken ct)
    {
        var customer = await TryReadCustomerByReferenceAsync(userId, ct);
        if (customer?.Id is null)
        {
            // No Maxio customer yet means this user has never subscribed -- not an error.
            return Array.Empty<UserSubscription>();
        }

        var subscriptions = await ListSubscriptionsForCustomerAsync(customer.Id.Value, ct);
        return subscriptions.Select(MapSubscription).ToList();
    }

    private async Task<Customer> EnsureCustomerAsync(string userId, string userEmail, CancellationToken ct)
    {
        var existing = await TryReadCustomerByReferenceAsync(userId, ct);
        if (existing is not null)
        {
            return existing;
        }

        var localPart = userEmail.Split('@')[0];
        var createRequest = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = localPart,
                LastName = "eShopOnWeb Customer",
                Email = userEmail,
                Reference = userId
            }
        };

        try
        {
            var response = await _client.Customers.CreateCustomer(createRequest, ct);
            if (response.Customer is null)
            {
                throw new MaxioBillingException("Billing provider returned an empty customer.", 502);
            }
            return response.Customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                // The typed 422 payload here doesn't reliably describe what went wrong (see maxio-plan.md
                // Assumptions & Blockers) -- a racing double-click is the expected cause, so re-check
                // by reference rather than trusting the error body's shape.
                var reLookup = await TryReadCustomerByReferenceAsync(userId, ct);
                if (reLookup is not null)
                {
                    return reLookup;
                }
                throw new MaxioBillingException("Unable to create billing customer.", 502, ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new MaxioBillingException($"Unable to create billing customer: {raw.ReadAsString()}", 502, ex);
            }
            throw new MaxioBillingException("Unable to create billing customer.", 502, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            var reconciled = await TryReadCustomerByReferenceAsync(userId, ct);
            if (reconciled is not null)
            {
                return reconciled;
            }
            throw new MaxioBillingException("Billing provider unreachable.", 502, ex);
        }
        catch (JsonException ex)
        {
            var reconciled = await TryReadCustomerByReferenceAsync(userId, ct);
            if (reconciled is not null)
            {
                return reconciled;
            }
            throw new MaxioBillingException("The billing provider returned a response that could not be processed.", 502, ex);
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
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw new MaxioBillingException($"Unable to look up billing customer: {ex.Error.ReadAsString()}", 502, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new MaxioBillingException("Billing provider unreachable.", 502, ex);
        }
        catch (JsonException ex)
        {
            throw new MaxioBillingException("The billing provider returned a response that could not be processed.", 502, ex);
        }
    }

    private async Task<Subscription?> FindSubscriptionAsync(int customerId, string planHandle, CancellationToken ct)
    {
        var subscriptions = await ListSubscriptionsForCustomerAsync(customerId, ct);
        return subscriptions.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase)
            && s.State != SubscriptionState.Canceled
            && s.State != SubscriptionState.Expired);
    }

    private async Task<IReadOnlyList<Subscription>> ListSubscriptionsForCustomerAsync(int customerId, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ListCustomerSubscriptions(customerId, ct);
            return response
                .Select(r => r.Subscription)
                .Where(s => s is not null)
                .Select(s => s!)
                .ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw new MaxioBillingException($"Unable to list subscriptions: {ex.Error.ReadAsString()}", 502, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new MaxioBillingException("Billing provider unreachable.", 502, ex);
        }
        catch (JsonException ex)
        {
            throw new MaxioBillingException("The billing provider returned a response that could not be processed.", 502, ex);
        }
    }

    private async Task<int> ResolveProductFamilyIdAsync(CancellationToken ct)
    {
        var cacheKey = ProductFamilyIdCacheKeyPrefix + _options.ProductFamilyHandle;
        if (_cache.TryGetValue(cacheKey, out int cachedId))
        {
            return cachedId;
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
            throw new MaxioBillingException($"Unable to resolve product family: {ex.Error.ReadAsString()}", 502, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new MaxioBillingException("Billing provider unreachable.", 502, ex);
        }
        catch (JsonException ex)
        {
            throw new MaxioBillingException("The billing provider returned a response that could not be processed.", 502, ex);
        }

        var match = families
            .Select(f => f.ProductFamily)
            .FirstOrDefault(f => f is not null && string.Equals(f.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase));

        if (match?.Id is null)
        {
            throw new MaxioBillingException($"No product family with handle '{_options.ProductFamilyHandle}' was found.", 404);
        }

        _cache.Set(cacheKey, match.Id.Value, CacheDuration);
        return match.Id.Value;
    }

    private async Task<string> ResolveDisplayCurrencyAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue(SiteCurrencyCacheKey, out string? cached) && cached is not null)
        {
            return cached;
        }

        try
        {
            var response = await _client.Sites.ReadSite(ct);
            var currency = response.Site.Currency ?? string.Empty;
            _cache.Set(SiteCurrencyCacheKey, currency, CacheDuration);
            return currency;
        }
        catch (SdkException<RawError> ex)
        {
            throw new MaxioBillingException($"Unable to resolve site currency: {ex.Error.ReadAsString()}", 502, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new MaxioBillingException("Billing provider unreachable.", 502, ex);
        }
        catch (JsonException ex)
        {
            throw new MaxioBillingException("The billing provider returned a response that could not be processed.", 502, ex);
        }
    }

    private static SubscriptionPlan MapPlan(Product product, string currency) => new()
    {
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? string.Empty,
        Price = (product.PriceInCents ?? 0) / 100m,
        Currency = currency,
        IntervalCount = product.Interval ?? 1,
        IntervalUnit = product.IntervalUnit?.Value ?? string.Empty
    };

    private static UserSubscription MapSubscription(Subscription subscription) => new()
    {
        MaxioSubscriptionId = subscription.Id ?? 0,
        PlanHandle = subscription.Product?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? string.Empty,
        Price = (subscription.CurrentBillingAmountInCents ?? subscription.Product?.PriceInCents ?? 0) / 100m,
        Currency = subscription.Currency ?? string.Empty,
        State = subscription.State?.Value ?? string.Empty,
        NextBillingDate = subscription.NextAssessmentAt
    };
}
