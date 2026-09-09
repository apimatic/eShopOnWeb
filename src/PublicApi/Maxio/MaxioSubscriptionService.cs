using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface IMaxioSubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken);

    Task<SubscriptionSummaryDto> SubscribeAsync(MaxioUserInfo user, string planHandle, CancellationToken cancellationToken);

    Task<IReadOnlyList<SubscriptionSummaryDto>> GetMySubscriptionsAsync(MaxioUserInfo user, CancellationToken cancellationToken);
}

/// <summary>
/// Fronts the Maxio Advanced Billing SDK for eShopOnWeb subscription billing.
/// Idempotency: Maxio customers are keyed on the eShop user id (customer
/// reference), subscriptions on a deterministic per-user-per-plan reference,
/// and concurrent subscribes for the same user+plan are serialized in-process,
/// so a double-click never creates two customers or two subscriptions.
/// </summary>
public sealed class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private const int TotalCallBudgetSeconds = 30;
    private const int ProductsPerPage = 100;
    private static readonly TimeSpan FamilyIdCacheLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan PlanCacheLifetime = TimeSpan.FromMinutes(2);

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscribeLocks = new();

    private readonly MaxioAdvancedBilling.MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly IMemoryCache _cache;
    private readonly TimeSpan _totalCallBudget = TimeSpan.FromSeconds(TotalCallBudgetSeconds);

    public MaxioSubscriptionService(
        MaxioAdvancedBilling.MaxioAdvancedBillingClient client,
        IOptions<MaxioOptions> options,
        IMemoryCache cache)
    {
        _client = client;
        _options = options.Value;
        _cache = cache;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
    {
        return await Bounded(async boundedCt =>
        {
            var productFamilyId = await ResolveProductFamilyIdAsync(boundedCt);
            var products = await ListAllFamilyProductsAsync(productFamilyId, boundedCt);

            return products
                .Where(p => p.ArchivedAt is null && !string.IsNullOrEmpty(p.Handle))
                .Select(p => new SubscriptionPlanDto(
                    p.Handle!,
                    p.Name ?? p.Handle!,
                    p.PriceInCents ?? 0,
                    p.Interval is null ? null : $"{p.Interval}",
                    p.IntervalUnit?.Value,
                    p.RequestCreditCard,
                    p.RequireCreditCard))
                .ToList();
        }, cancellationToken);
    }

    public async Task<SubscriptionSummaryDto> SubscribeAsync(MaxioUserInfo user, string planHandle, CancellationToken cancellationToken)
    {
        return await Bounded(async boundedCt =>
        {
            var plan = (await GetPlansCachedAsync(boundedCt))
                .FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase))
                ?? throw new MaxioBillingException(404, $"Unknown subscription plan '{planHandle}'.");

            var subscriptionReference = BuildSubscriptionReference(user.UserId, plan.Handle);
            var gate = SubscribeLocks.GetOrAdd(subscriptionReference, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(boundedCt);
            try
            {
                var existing = await FindSubscriptionByReferenceAsync(subscriptionReference, boundedCt);
                if (existing is not null)
                {
                    return MapSubscription(existing, plan.Handle, plan.Name, alreadySubscribed: true);
                }

                var customer = await EnsureCustomerAsync(user, boundedCt);
                var created = await CreateSubscriptionAsync(subscriptionReference, plan.Handle, customer, boundedCt);
                return MapSubscription(created, plan.Handle, plan.Name, alreadySubscribed: false);
            }
            finally
            {
                gate.Release();
            }
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<SubscriptionSummaryDto>> GetMySubscriptionsAsync(MaxioUserInfo user, CancellationToken cancellationToken)
    {
        return await Bounded<IReadOnlyList<SubscriptionSummaryDto>>(async boundedCt =>
        {
            var customer = await FindCustomerByReferenceAsync(BuildCustomerReference(user.UserId), boundedCt);
            if (customer?.Id is null)
            {
                return Array.Empty<SubscriptionSummaryDto>();
            }

            IReadOnlyList<SubscriptionResponse> subscriptions;
            try
            {
                subscriptions = await _client.Customers.ListCustomerSubscriptions(customer.Id.Value, boundedCt);
            }
            catch (SdkException<RawError> ex)
            {
                throw WrapRawError(ex.Error, "list subscriptions");
            }

            return subscriptions
                .Select(s => s.Subscription)
                .Where(s => s is not null)
                .Select(s => MapSubscription(s!, s!.Product?.Handle ?? string.Empty, s.Product?.Name ?? string.Empty, alreadySubscribed: true))
                .ToList();
        }, cancellationToken);
    }

    private async Task<string> ResolveProductFamilyIdAsync(CancellationToken ct)
    {
        return (await _cache.GetOrCreateAsync(CacheKeys.ProductFamilyId(_options.ProductFamilyHandle), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = FamilyIdCacheLifetime;

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
                throw WrapRawError(ex.Error, "list product families");
            }

            var match = families
                .Select(f => f.ProductFamily)
                .FirstOrDefault(f => f is not null &&
                    string.Equals(f.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase));

            if (match?.Id is null)
            {
                throw new MaxioBillingException(502,
                    $"The configured Maxio product family '{_options.ProductFamilyHandle}' was not found in the billing site.");
            }

            return match.Id.Value.ToString();
        }))!;
    }

    private async Task<IReadOnlyList<Product>> ListAllFamilyProductsAsync(string productFamilyId, CancellationToken ct)
    {
        var products = new List<Product>();
        var page = 1;
        while (true)
        {
            IReadOnlyList<ProductResponse> batch;
            try
            {
                batch = await _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId,
                    dateField: null,
                    filter: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    includeArchived: false,
                    include: null,
                    page: page,
                    perPage: ProductsPerPage,
                    ct: ct);
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetString(out _))
                {
                    throw new MaxioBillingException(502,
                        $"The configured Maxio product family '{_options.ProductFamilyHandle}' was not found in the billing site.");
                }
                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw WrapRawError(raw, "list subscription plans");
                }
                throw new MaxioBillingException(502, "The billing provider rejected the request to list subscription plans.");
            }

            products.AddRange(batch.Select(b => b.Product).Where(p => p is not null).Select(p => p!));
            if (batch.Count < ProductsPerPage)
            {
                break;
            }
            page++;
        }
        return products;
    }

    private async Task<MaxioAdvancedBilling.Models.Customer?> FindCustomerByReferenceAsync(string reference, CancellationToken ct)
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
            throw WrapRawError(ex.Error, "look up customer");
        }
    }

    private async Task<MaxioAdvancedBilling.Models.Customer> EnsureCustomerAsync(MaxioUserInfo user, CancellationToken ct)
    {
        var reference = BuildCustomerReference(user.UserId);

        var existing = await FindCustomerByReferenceAsync(reference, ct);
        if (existing?.Id is not null)
        {
            return existing;
        }

        try
        {
            var created = await _client.Customers.CreateCustomer(
                new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = user.FirstName,
                        LastName = user.LastName,
                        Email = user.Email,
                        Reference = reference
                    }
                },
                ct);
            return created.Customer
                ?? throw new MaxioBillingException(502, "The billing provider returned an empty customer after creation.");
        }
        catch (SdkException<CreateCustomerError> ex) when (ex.Error.TryGetCustomerErrorResponse1(out _))
        {
            var racedCustomer = await FindCustomerByReferenceAsync(reference, ct);
            if (racedCustomer?.Id is not null)
            {
                return racedCustomer;
            }
            throw new MaxioBillingException(502, $"The billing provider rejected creating the customer for {user.Email}.");
        }
        catch (SdkException<CreateCustomerError> ex) when (ex.Error.TryGetRawError(out var raw))
        {
            throw WrapRawError(raw, "create customer");
        }
    }

    private async Task<Subscription?> FindSubscriptionByReferenceAsync(string subscriptionReference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Subscriptions.FindSubscription(subscriptionReference, ct);
            return response.Subscription;
        }
        catch (SdkException<FindSubscriptionError> ex) when (ex.Error.TryGetNoContent(out _))
        {
            return null;
        }
        catch (SdkException<FindSubscriptionError> ex) when (ex.Error.TryGetRawError(out var raw))
        {
            throw WrapRawError(raw, "look up subscription");
        }
    }

    private async Task<Subscription> CreateSubscriptionAsync(string subscriptionReference, string productHandle, MaxioAdvancedBilling.Models.Customer customer, CancellationToken ct)
    {
        if (customer.Id is null)
        {
            throw new MaxioBillingException(502, "The billing customer record has no id; cannot subscribe.");
        }

        try
        {
            var response = await _client.Subscriptions.CreateSubscription(
                new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = productHandle,
                        CustomerId = customer.Id,
                        Reference = subscriptionReference,
                        // Card-free enrollment: the seeded plans carry request_credit_card=true /
                        // require_credit_card=false, so automatic collection still demands a
                        // payment profile for the balance (422). Remittance enrolls without one.
                        PaymentCollectionMethod = MaxioAdvancedBilling.Models.Enums.CollectionMethod.Remittance
                    }
                },
                ct);

            return response.Subscription
                ?? throw new MaxioBillingException(502, "The billing provider returned an empty subscription after creation.");
        }
        catch (SdkException<CreateSubscriptionError> ex) when (ex.Error.TryGetErrorListResponse1(out var errorList))
        {
            var reasons = errorList.Errors is { Count: > 0 } ? string.Join("; ", errorList.Errors) : "no details provided";
            throw new MaxioBillingException(502, $"The billing provider rejected the subscription: {reasons}");
        }
        catch (SdkException<CreateSubscriptionError> ex) when (ex.Error.TryGetRawError(out var raw))
        {
            throw WrapRawError(raw, "create subscription");
        }
    }

    private async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansCachedAsync(CancellationToken ct)
    {
        return (await _cache.GetOrCreateAsync(CacheKeys.Plans, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = PlanCacheLifetime;
            return await GetPlansAsync(ct);
        }))!;
    }

    private static SubscriptionSummaryDto MapSubscription(Subscription subscription, string planHandle, string planName, bool alreadySubscribed)
    {
        return new SubscriptionSummaryDto(
            subscription.Id ?? 0,
            subscription.Product?.Handle ?? planHandle,
            subscription.Product?.Name ?? planName,
            subscription.ProductPriceInCents ?? 0,
            subscription.State?.Value ?? "unknown",
            subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
            subscription.Currency,
            subscription.Reference,
            alreadySubscribed);
    }

    private static MaxioBillingException WrapRawError(RawError raw, string operation)
    {
        var status = (int)raw.StatusCode;
        var message = status switch
        {
            401 or 403 => "The billing provider rejected the configured credentials.",
            404 => $"The billing provider could not find a resource while trying to {operation}.",
            429 => "The billing provider is rate limiting requests. Please retry shortly.",
            _ => $"The billing provider returned an error (HTTP {status}) while trying to {operation}."
        };
        return new MaxioBillingException(status, message);
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_totalCallBudget);
        try
        {
            return await call(cts.Token);
        }
        catch (MaxioBillingException)
        {
            throw;
        }
        catch (JsonException)
        {
            throw new MaxioBillingException(null, "The billing provider returned a response that could not be processed.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new MaxioBillingException(null, "The billing provider is unreachable. Please retry later.");
        }
    }

    private static string BuildCustomerReference(string userId) => $"eshop-user-{userId}";

    private static string BuildSubscriptionReference(string userId, string planHandle) =>
        $"eshop-sub-{userId}-{planHandle}";

    private static class CacheKeys
    {
        public static string ProductFamilyId(string handle) => $"maxio:family-id:{handle}";
        public const string Plans = "maxio:plans";
    }
}
