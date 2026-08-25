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
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.eShopWeb.PublicApi.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Services;

/// <summary>
/// Fronts the Maxio Advanced Billing SDK for the subscription-billing capability.
/// Every SDK call is bounded by a whole-call cancellation budget and translated into
/// <see cref="MaxioIntegrationException"/> at this boundary.
/// </summary>
public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    public const string HttpClientName = "Maxio";

    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PlanCacheDuration = TimeSpan.FromMinutes(5);
    private const string PlanCacheKey = "maxio:subscription-plans";

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioSettings> settings,
        IMemoryCache cache,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(PlanCacheKey, out IReadOnlyList<SubscriptionPlanDto>? cached) && cached is not null)
        {
            return cached;
        }

        var familyId = await ResolveProductFamilyIdAsync(cancellationToken);

        IReadOnlyList<ProductResponse> products;
        try
        {
            products = await BoundedAsync(ct => _client.ProductFamilies.ListProductsForProductFamily(
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
                perPage: 20,
                ct: ct), cancellationToken);
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out var message))
            {
                throw new MaxioIntegrationException(404, message, ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw Translate(raw, ex);
            }
            throw new MaxioIntegrationException(502, "The billing provider could not list the subscription plans.", ex);
        }

        var plans = products
            .Select(p => p.Product)
            .Where(p => p is not null && p.ArchivedAt is null)
            .Select(p => new SubscriptionPlanDto
            {
                Name = p!.Name ?? string.Empty,
                Handle = p.Handle ?? string.Empty,
                Description = p.Description,
                PriceInCents = p.PriceInCents ?? 0,
                Interval = p.Interval ?? 1,
                IntervalUnit = p.IntervalUnit?.Value ?? string.Empty,
            })
            .ToList();

        _cache.Set(PlanCacheKey, plans, PlanCacheDuration);
        return plans;
    }

    public async Task<SubscriptionDto> SubscribeAsync(SubscribeCommand command, CancellationToken cancellationToken = default)
    {
        var customer = await FindOrCreateCustomerAsync(command, cancellationToken);
        if (customer.Id is not int customerId)
        {
            throw new MaxioIntegrationException(502, "The billing provider returned a customer without an id.");
        }

        // Deterministic reference makes a retried/duplicated subscribe converge on one subscription.
        var subscriptionReference = $"{command.CustomerReference}:{command.ProductHandle}";

        var existing = await FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
        if (existing is not null)
        {
            return Map(existing);
        }

        var request = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = command.ProductHandle,
                CustomerId = customerId,
                Reference = subscriptionReference,
                // Cardless signup: bill by invoice instead of attempting automatic collection,
                // which 422s on a paid plan with no payment method on file.
                PaymentCollectionMethod = CollectionMethod.Remittance,
            }
        };

        try
        {
            var response = await BoundedAsync(ct => _client.Subscriptions.CreateSubscription(body: request, ct: ct), cancellationToken);
            if (response.Subscription is null)
            {
                throw new MaxioIntegrationException(502, "The billing provider returned an empty subscription.");
            }
            return Map(response.Subscription);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                var detail = errorList.Errors is { Count: > 0 } ? string.Join("; ", errorList.Errors) : "The subscription was rejected.";
                throw new MaxioIntegrationException(422, detail, ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw Translate(raw, ex);
            }
            throw new MaxioIntegrationException(502, "The billing provider rejected the subscription.", ex);
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListSubscriptionsAsync(string customerReference, CancellationToken cancellationToken = default)
    {
        Customer? customer;
        try
        {
            var response = await BoundedAsync(ct => _client.Customers.ReadCustomerByReference(reference: customerReference, ct: ct), cancellationToken);
            customer = response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return Array.Empty<SubscriptionDto>();
        }

        if (customer?.Id is not int customerId)
        {
            return Array.Empty<SubscriptionDto>();
        }

        try
        {
            var subscriptions = await BoundedAsync(ct => _client.Customers.ListCustomerSubscriptions(customerId: customerId, ct: ct), cancellationToken);
            return subscriptions
                .Select(s => s.Subscription)
                .Where(s => s is not null)
                .Select(s => Map(s!))
                .ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex.Error, ex);
        }
    }

    private async Task<int> ResolveProductFamilyIdAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ProductFamilyResponse> families;
        try
        {
            families = await BoundedAsync(ct => _client.ProductFamilies.ListProductFamilies(
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                ct: ct), cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex.Error, ex);
        }

        var family = families
            .Select(f => f.ProductFamily)
            .FirstOrDefault(f => f?.Handle == _settings.ProductFamilyHandle);

        if (family?.Id is not int familyId)
        {
            throw new MaxioIntegrationException(502, $"The configured product family '{_settings.ProductFamilyHandle}' was not found at the billing provider.");
        }
        return familyId;
    }

    private async Task<Customer> FindOrCreateCustomerAsync(SubscribeCommand command, CancellationToken cancellationToken)
    {
        try
        {
            var response = await BoundedAsync(ct => _client.Customers.ReadCustomerByReference(reference: command.CustomerReference, ct: ct), cancellationToken);
            if (response.Customer is not null)
            {
                return response.Customer;
            }
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            // fall through to create
        }

        var request = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = command.FirstName,
                LastName = command.LastName,
                Email = command.Email,
                Reference = command.CustomerReference,
            }
        };

        try
        {
            var created = await BoundedAsync(ct => _client.Customers.CreateCustomer(body: request, ct: ct), cancellationToken);
            if (created.Customer is null)
            {
                throw new MaxioIntegrationException(502, "The billing provider returned an empty customer.");
            }
            return created.Customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                // 422 — most likely a concurrent create won the reference-uniqueness race; re-read the winner.
                var winner = await BoundedAsync(ct => _client.Customers.ReadCustomerByReference(reference: command.CustomerReference, ct: ct), cancellationToken);
                if (winner.Customer is not null)
                {
                    return winner.Customer;
                }
                throw new MaxioIntegrationException(422, "The billing provider rejected the customer.", ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw Translate(raw, ex);
            }
            throw new MaxioIntegrationException(502, "The billing provider rejected the customer.", ex);
        }
    }

    private async Task<Subscription?> FindSubscriptionByReferenceAsync(string subscriptionReference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await BoundedAsync(ct => _client.Subscriptions.FindSubscription(reference: subscriptionReference, ct: ct), cancellationToken);
            return response.Subscription;
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                return null; // 404 — no such subscription yet
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw Translate(raw, ex);
            }
            throw new MaxioIntegrationException(502, "The billing provider could not look up the subscription.", ex);
        }
    }

    private static SubscriptionDto Map(Subscription subscription) => new()
    {
        Id = subscription.Id ?? 0,
        Reference = subscription.Reference,
        State = subscription.State?.Value ?? string.Empty,
        ProductName = subscription.Product?.Name ?? string.Empty,
        ProductHandle = subscription.Product?.Handle ?? string.Empty,
        PriceInCents = subscription.ProductPriceInCents,
        NextBillingDate = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
    };

    // Bounds the whole call (the per-attempt knobs on the client do not) and converts
    // non-SDK failures. SdkException<T> is deliberately left to the per-operation catches above.
    private async Task<T> BoundedAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        try
        {
            return await call(cts.Token);
        }
        // SdkException<T> and MaxioIntegrationException fall through to the per-operation catches.
        catch (JsonException ex)
        {
            throw new MaxioIntegrationException(502, "The billing provider returned a response that could not be processed.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new MaxioIntegrationException(502, "The billing provider is unreachable or timed out.", ex);
        }
    }

    private MaxioIntegrationException Translate(RawError raw, Exception inner)
    {
        var status = (int)raw.StatusCode;
        _logger.LogWarning("Maxio call failed with HTTP {StatusCode}", status);
        // Carry provider 4xx through; anything else is a provider-side/unknown failure.
        var surface = status is >= 400 and < 500 ? status : 502;
        return new MaxioIntegrationException(surface, $"The billing provider returned HTTP {status}.", inner);
    }
}
