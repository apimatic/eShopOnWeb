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
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class SubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    // Subscriptions in these states do not block re-subscribing to the same plan.
    private static readonly SubscriptionState[] NonBlockingStates =
    {
        SubscriptionState.Canceled,
        SubscriptionState.Expired,
        SubscriptionState.TrialEnded,
        SubscriptionState.FailedToCreate
    };

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly IMemoryCache _cache;
    private readonly ILogger<SubscriptionBillingService> _logger;

    public SubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioSettings> settings,
        IMemoryCache cache,
        ILogger<SubscriptionBillingService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _cache = cache;
        _logger = logger;
    }

    public Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken = default)
        => Guarded(ListPlansCore, cancellationToken);

    public Task<SubscribeResult> SubscribeAsync(string userReference, string email, string productHandle, CancellationToken cancellationToken = default)
        => Guarded(ct => SubscribeCore(userReference, email, productHandle, ct), cancellationToken);

    public Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(string userReference, CancellationToken cancellationToken = default)
        => Guarded(ct => ListMySubscriptionsCore(userReference, ct), cancellationToken);

    private async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansCore(CancellationToken ct)
    {
        var familyId = await GetProductFamilyIdAsync(ct);

        var plans = new List<SubscriptionPlanDto>();
        const int perPage = 200;
        for (var page = 1; ; page++)
        {
            var currentPage = page;
            var products = await Bounded(c => _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: familyId.ToString(),
                dateField: null,
                filter: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                includeArchived: false,
                include: null,
                page: currentPage,
                perPage: perPage,
                ct: c), ct);

            foreach (var productResponse in products)
            {
                var product = productResponse.Product;
                if (product.ArchivedAt is not null)
                {
                    continue;
                }

                plans.Add(new SubscriptionPlanDto
                {
                    Id = product.Id,
                    Name = product.Name,
                    Handle = product.Handle,
                    Description = product.Description,
                    PriceInCents = product.PriceInCents,
                    Interval = product.Interval,
                    IntervalUnit = product.IntervalUnit?.Value
                });
            }

            if (products.Count < perPage)
            {
                break;
            }
        }

        return plans;
    }

    private async Task<SubscribeResult> SubscribeCore(string userReference, string email, string productHandle, CancellationToken ct)
    {
        var plans = await ListPlansCore(ct);
        if (!plans.Any(p => string.Equals(p.Handle, productHandle, StringComparison.OrdinalIgnoreCase)))
        {
            throw new BillingException(HttpStatusCode.NotFound,
                $"No subscription plan with handle '{productHandle}' exists.");
        }

        var customerId = await GetOrCreateCustomerIdAsync(userReference, email, ct);

        var existing = await Bounded(c => _client.Customers.ListCustomerSubscriptions(customerId: customerId, ct: c), ct);
        var live = existing
            .Select(r => r.Subscription)
            .FirstOrDefault(s => s is not null
                && string.Equals(s.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase)
                && !IsNonBlocking(s.State));

        if (live is not null)
        {
            return new SubscribeResult(Map(live!), AlreadyExisted: true);
        }

        var created = await Bounded(c => _client.Subscriptions.CreateSubscription(
            body: new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = productHandle,
                    CustomerId = customerId,
                    Reference = $"{userReference}:{productHandle}",
                    // Remittance bills without card capture: the default (automatic)
                    // collection demands a payment profile on file for the signup balance.
                    PaymentCollectionMethod = CollectionMethod.Remittance
                }
            },
            ct: c), ct);

        if (created.Subscription is null)
        {
            throw new BillingException(HttpStatusCode.BadGateway,
                "The billing provider returned an empty subscription response.");
        }

        return new SubscribeResult(Map(created.Subscription), AlreadyExisted: false);
    }

    private async Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsCore(string userReference, CancellationToken ct)
    {
        CustomerResponse customer;
        try
        {
            customer = await Bounded(c => _client.Customers.ReadCustomerByReference(reference: userReference, ct: c), ct);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return Array.Empty<SubscriptionDto>();
        }

        if (customer.Customer?.Id is not int customerId)
        {
            throw new BillingException(HttpStatusCode.BadGateway,
                "The billing provider returned a customer without an id.");
        }

        var subscriptions = await Bounded(c => _client.Customers.ListCustomerSubscriptions(customerId: customerId, ct: c), ct);
        return subscriptions
            .Select(r => r.Subscription)
            .Where(s => s is not null)
            .Select(s => Map(s!))
            .ToList();
    }

    private async Task<int> GetOrCreateCustomerIdAsync(string userReference, string email, CancellationToken ct)
    {
        try
        {
            var existing = await Bounded(c => _client.Customers.ReadCustomerByReference(reference: userReference, ct: c), ct);
            if (existing.Customer?.Id is int existingId)
            {
                return existingId;
            }

            throw new BillingException(HttpStatusCode.BadGateway,
                "The billing provider returned a customer without an id.");
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            // Not found is the find-or-create branch signal, not a failure.
        }

        var (firstName, lastName) = DeriveNames(userReference);
        try
        {
            var created = await Bounded(c => _client.Customers.CreateCustomer(
                body: new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = firstName,
                        LastName = lastName,
                        Email = email,
                        Reference = userReference
                    }
                },
                ct: c), ct);

            if (created.Customer?.Id is int newId)
            {
                return newId;
            }

            throw new BillingException(HttpStatusCode.BadGateway,
                "The billing provider returned a customer without an id.");
        }
        catch (SdkException<CreateCustomerError> ex) when (ex.Error.TryGetCustomerErrorResponse1(out _))
        {
            // 422 on create: a concurrent request likely won the create race for this
            // reference. Re-read; if the customer is still absent, surface the original 422.
            var reread = await Bounded(c => _client.Customers.ReadCustomerByReference(reference: userReference, ct: c), ct);
            if (reread.Customer?.Id is int raceId)
            {
                return raceId;
            }

            throw;
        }
    }

    private Task<int> GetProductFamilyIdAsync(CancellationToken ct)
    {
        var cacheKey = $"maxio:product-family:{_settings.ProductFamilyHandle}";
        return _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);

            var families = await Bounded(c => _client.ProductFamilies.ListProductFamilies(
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                ct: c), ct);

            var match = families.FirstOrDefault(f =>
                string.Equals(f.ProductFamily?.Handle, _settings.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase));

            if (match?.ProductFamily?.Id is not int id)
            {
                throw new BillingException(HttpStatusCode.BadGateway,
                    "The configured Maxio product family was not found.");
            }

            return id;
        })!;
    }

    private static bool IsNonBlocking(SubscriptionState? state)
        => state is not null && NonBlockingStates.Contains(state);

    private static SubscriptionDto Map(Subscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State?.Value,
        ProductName = subscription.Product?.Name,
        ProductHandle = subscription.Product?.Handle,
        PriceInCents = subscription.ProductPriceInCents,
        NextBillingDate = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
        Reference = subscription.Reference,
        CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod
    };

    private static (string FirstName, string LastName) DeriveNames(string userReference)
    {
        var local = userReference.Split('@')[0].Trim();
        return (string.IsNullOrEmpty(local) ? "Customer" : local, "eShopOnWeb");
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private async Task<T> Guarded<T>(Func<CancellationToken, Task<T>> core, CancellationToken ct)
    {
        try
        {
            return await core(ct);
        }
        catch (Exception ex) when (ex is not BillingException)
        {
            throw Translate(ex);
        }
    }

    private BillingException Translate(Exception ex)
    {
        switch (ex)
        {
            case SdkException<ListProductsForProductFamilyError> listProducts:
                if (listProducts.Error.TryGetString(out var notFoundMessage))
                {
                    _logger.LogWarning($"Maxio product family lookup failed: {notFoundMessage}");
                    return new BillingException(HttpStatusCode.BadGateway,
                        "The configured Maxio product family was not found.", listProducts);
                }
                if (listProducts.Error.TryGetRawError(out var listRaw))
                {
                    return FromRaw(listRaw, listProducts);
                }
                return new BillingException(HttpStatusCode.BadGateway, "Billing provider error.", listProducts);

            case SdkException<CreateCustomerError> createCustomer:
                // The typed 422 payload model is a known shared-model artifact; do not trust
                // its fields for messages.
                if (createCustomer.Error.TryGetCustomerErrorResponse1(out _))
                {
                    return new BillingException(HttpStatusCode.UnprocessableEntity,
                        "Maxio rejected the customer record.", createCustomer);
                }
                if (createCustomer.Error.TryGetRawError(out var customerRaw))
                {
                    return FromRaw(customerRaw, createCustomer);
                }
                return new BillingException(HttpStatusCode.BadGateway, "Billing provider error.", createCustomer);

            case SdkException<CreateSubscriptionError> createSubscription:
                if (createSubscription.Error.TryGetErrorListResponse1(out var errorList))
                {
                    return new BillingException(HttpStatusCode.UnprocessableEntity,
                        $"Maxio rejected the subscription: {string.Join("; ", errorList.Errors)}", createSubscription);
                }
                if (createSubscription.Error.TryGetRawError(out var subscriptionRaw))
                {
                    return FromRaw(subscriptionRaw, createSubscription);
                }
                return new BillingException(HttpStatusCode.BadGateway, "Billing provider error.", createSubscription);

            case SdkException<RawError> rawError:
                return FromRaw(rawError.Error, rawError);

            case JsonException jsonException:
                _logger.LogError(jsonException, "Maxio returned a response that could not be deserialized.");
                return new BillingException(HttpStatusCode.BadGateway,
                    "The billing provider returned a response that could not be processed.", jsonException);

            case HttpRequestException:
            case TaskCanceledException:
                _logger.LogError(ex, "Maxio call failed at the transport level.");
                return new BillingException(HttpStatusCode.ServiceUnavailable,
                    "The billing provider is unreachable or did not respond in time.", ex);

            default:
                _logger.LogError(ex, "Unexpected Maxio integration failure.");
                return new BillingException(HttpStatusCode.BadGateway,
                    "Unexpected billing provider failure.", ex);
        }
    }

    private BillingException FromRaw(RawError raw, Exception inner)
    {
        var status = raw.StatusCode;
        var body = raw.ReadAsString();
        _logger.LogWarning($"Maxio responded {(int)status}: {body}");

        // Provider 4xx is actionable by the caller — carry the status and detail.
        // 5xx/unknown collapses to 502 with a caller-safe message.
        if ((int)status >= 400 && (int)status < 500)
        {
            return new BillingException(status,
                $"Billing provider rejected the request: {Truncate(body)}", inner);
        }

        return new BillingException(HttpStatusCode.BadGateway, "Billing provider error.", inner);
    }

    private static string Truncate(string value, int max = 500)
        => value.Length <= max ? value : value.Substring(0, max);
}
