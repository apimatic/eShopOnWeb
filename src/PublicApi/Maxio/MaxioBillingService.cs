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
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class MaxioBillingService : IMaxioBillingService
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(20);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly IRepository<MaxioCustomerMapping> _customerMappings;
    private readonly MaxioOptions _options;

    public MaxioBillingService(MaxioAdvancedBillingClient client, IRepository<MaxioCustomerMapping> customerMappings, IOptions<MaxioOptions> options)
    {
        _client = client;
        _customerMappings = customerMappings;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken ct)
    {
        var products = await ExecuteAsync(async innerCt =>
        {
            try
            {
                return await _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: $"handle:{_options.ProductFamilyHandle}",
                    dateField: null,
                    filter: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    includeArchived: null,
                    include: null,
                    page: 1,
                    perPage: 50,
                    ct: innerCt);
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetString(out var notFoundMessage))
                    throw new MaxioIntegrationException(HttpStatusCode.NotFound, notFoundMessage, ex);
                if (ex.Error.TryGetRawError(out var raw))
                    throw new MaxioIntegrationException(MapStatus(raw.StatusCode), SafeBody(raw), ex);
                throw new MaxioIntegrationException(HttpStatusCode.BadGateway, "Unexpected billing provider error.", ex);
            }
        }, ct);

        var plans = new List<SubscriptionPlanDto>();
        foreach (var item in products)
        {
            var product = item.Product;
            if (product is null) continue;
            plans.Add(new SubscriptionPlanDto
            {
                Id = product.Id ?? 0,
                Handle = product.Handle ?? string.Empty,
                Name = product.Name ?? string.Empty,
                Price = (product.PriceInCents ?? 0) / 100m,
                Interval = product.Interval ?? 0,
                IntervalUnit = product.IntervalUnit?.Value ?? string.Empty,
                RequiresCreditCard = product.RequireCreditCard ?? false
            });
        }
        return plans;
    }

    public async Task<SubscriptionDto> SubscribeAsync(string userName, string planHandle, CancellationToken ct)
    {
        var customerId = await EnsureCustomerAsync(userName, ct);
        var subscriptionReference = BuildSubscriptionReference(userName, planHandle);

        var existing = await TryFindSubscriptionByReferenceAsync(subscriptionReference, ct)
            ?? await TryFindSubscriptionForPlanAsync(customerId, planHandle, ct);
        if (existing is not null) return MapSubscription(existing);

        var created = await ExecuteAsync(async innerCt =>
        {
            try
            {
                var response = await _client.Subscriptions.CreateSubscription(new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = planHandle,
                        CustomerId = customerId,
                        Reference = subscriptionReference,
                        // These plans require no payment method up front, so bill by invoice rather
                        // than attempting an automatic card charge (which fails with no card on file).
                        PaymentCollectionMethod = CollectionMethod.Invoice
                    }
                }, ct: innerCt);
                return response.Subscription;
            }
            catch (SdkException<CreateSubscriptionError> ex)
            {
                // A concurrent double-click may have created the subscription between our pre-check
                // above and this call - reconcile by re-reading before surfacing a real failure.
                var recovered = await TryFindSubscriptionByReferenceAsync(subscriptionReference, innerCt);
                if (recovered is not null) return recovered;

                throw new MaxioIntegrationException(HttpStatusCode.UnprocessableEntity, DescribeCreateSubscriptionError(ex.Error), ex);
            }
            catch (JsonException)
            {
                // Same reasoning as CreateCustomer above: the rejection body didn't match the
                // generated error shape, so reconcile by re-reading before giving up.
                var recovered = await TryFindSubscriptionByReferenceAsync(subscriptionReference, innerCt);
                if (recovered is not null) return recovered;
                throw;
            }
        }, ct);

        if (created is null)
            throw new MaxioIntegrationException(HttpStatusCode.BadGateway, "The billing provider did not return the created subscription.");

        return MapSubscription(created);
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(string userName, CancellationToken ct)
    {
        var customerId = await TryResolveCustomerIdAsync(userName, ct);
        if (customerId is null) return Array.Empty<SubscriptionDto>();

        var subscriptions = await ListCustomerSubscriptionsAsync(customerId.Value, ct);
        return subscriptions
            .Select(s => s.Subscription)
            .Where(s => s is not null)
            .Select(s => MapSubscription(s!))
            .ToList();
    }

    private async Task<int> EnsureCustomerAsync(string userName, CancellationToken ct)
    {
        var reference = BuildCustomerReference(userName);

        var mapping = await _customerMappings.FirstOrDefaultAsync(new MaxioCustomerMappingByUserNameSpecification(userName), ct);
        if (mapping is not null) return mapping.MaxioCustomerId;

        var existingCustomer = await TryReadCustomerByReferenceAsync(reference, ct);
        if (existingCustomer is not null)
        {
            await CacheCustomerMappingAsync(userName, reference, existingCustomer.Id ?? 0, ct);
            return existingCustomer.Id ?? 0;
        }

        // ApplicationUser carries no first/last name in this app; derive a reasonable display name
        // from the email local-part rather than leaving Maxio's required name fields blank.
        var localPart = userName.Contains('@') ? userName[..userName.IndexOf('@')] : userName;

        var created = await ExecuteAsync(async innerCt =>
        {
            try
            {
                var response = await _client.Customers.CreateCustomer(new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = localPart,
                        LastName = "eShopOnWeb Customer",
                        Email = userName,
                        Reference = reference
                    }
                }, ct: innerCt);
                return response.Customer;
            }
            catch (SdkException<CreateCustomerError> ex)
            {
                // A concurrent double-click may have won the race to create this reference first -
                // reconcile by re-reading before surfacing a real failure (Maxio enforces reference
                // uniqueness server-side, so the loser of the race lands here).
                var recovered = await TryReadCustomerByReferenceAsync(reference, innerCt);
                if (recovered is not null) return recovered;

                throw new MaxioIntegrationException(HttpStatusCode.Conflict, DescribeCreateCustomerError(ex.Error), ex);
            }
            catch (JsonException)
            {
                // The rejection body didn't match the generated error shape, so the SDK's own
                // exception construction failed and the real status was lost with it (see
                // dotnet-error-handling). This commonly means a concurrent request just won the
                // race to create this reference - reconcile the same way as above before giving up.
                var recovered = await TryReadCustomerByReferenceAsync(reference, innerCt);
                if (recovered is not null) return recovered;
                throw;
            }
        }, ct);

        if (created is null)
            throw new MaxioIntegrationException(HttpStatusCode.BadGateway, "The billing provider did not return the created customer.");

        await CacheCustomerMappingAsync(userName, reference, created.Id ?? 0, ct);
        return created.Id ?? 0;
    }

    private async Task<int?> TryResolveCustomerIdAsync(string userName, CancellationToken ct)
    {
        var mapping = await _customerMappings.FirstOrDefaultAsync(new MaxioCustomerMappingByUserNameSpecification(userName), ct);
        if (mapping is not null) return mapping.MaxioCustomerId;

        var reference = BuildCustomerReference(userName);
        var customer = await TryReadCustomerByReferenceAsync(reference, ct);
        if (customer is null) return null;

        await CacheCustomerMappingAsync(userName, reference, customer.Id ?? 0, ct);
        return customer.Id ?? 0;
    }

    private async Task CacheCustomerMappingAsync(string userName, string reference, int customerId, CancellationToken ct)
    {
        await _customerMappings.AddAsync(new MaxioCustomerMapping(userName, reference, customerId), ct);
    }

    private Task<Customer?> TryReadCustomerByReferenceAsync(string reference, CancellationToken ct) =>
        ExecuteAsync<Customer?>(async innerCt =>
        {
            try
            {
                var response = await _client.Customers.ReadCustomerByReference(reference, ct: innerCt);
                return response.Customer;
            }
            catch (SdkException<RawError> ex)
            {
                if (ex.Error.StatusCode == HttpStatusCode.NotFound) return null;
                throw new MaxioIntegrationException(MapStatus(ex.Error.StatusCode), SafeBody(ex.Error), ex);
            }
        }, ct);

    private Task<Subscription?> TryFindSubscriptionByReferenceAsync(string subscriptionReference, CancellationToken ct) =>
        ExecuteAsync<Subscription?>(async innerCt =>
        {
            try
            {
                var response = await _client.Subscriptions.FindSubscription(subscriptionReference, ct: innerCt);
                return response.Subscription;
            }
            catch (SdkException<FindSubscriptionError> ex)
            {
                if (ex.Error.TryGetNoContent(out _)) return null;
                if (ex.Error.TryGetRawError(out var raw))
                    throw new MaxioIntegrationException(MapStatus(raw.StatusCode), SafeBody(raw), ex);
                throw new MaxioIntegrationException(HttpStatusCode.BadGateway, "Unexpected billing provider error.", ex);
            }
        }, ct);

    private async Task<Subscription?> TryFindSubscriptionForPlanAsync(int customerId, string planHandle, CancellationToken ct)
    {
        var subscriptions = await ListCustomerSubscriptionsAsync(customerId, ct);
        return subscriptions
            .Select(s => s.Subscription)
            .FirstOrDefault(s => s is not null && string.Equals(s!.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
    }

    private Task<IReadOnlyList<SubscriptionResponse>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct) =>
        ExecuteAsync(async innerCt =>
        {
            try
            {
                return await _client.Customers.ListCustomerSubscriptions(customerId, ct: innerCt);
            }
            catch (SdkException<RawError> ex)
            {
                throw new MaxioIntegrationException(MapStatus(ex.Error.StatusCode), SafeBody(ex.Error), ex);
            }
        }, ct);

    private static SubscriptionDto MapSubscription(Subscription s) => new()
    {
        SubscriptionId = s.Id ?? 0,
        PlanName = s.Product?.Name ?? string.Empty,
        PlanHandle = s.Product?.Handle ?? string.Empty,
        Price = (s.CurrentBillingAmountInCents ?? s.ProductPriceInCents ?? 0) / 100m,
        Currency = s.Currency,
        State = s.State?.Value ?? string.Empty,
        NextBillingDate = s.NextAssessmentAt,
        CurrentPeriodEndsAt = s.CurrentPeriodEndsAt
    };

    private static HttpStatusCode MapStatus(HttpStatusCode providerStatus)
    {
        var code = (int)providerStatus;
        return code is >= 400 and < 500 ? providerStatus : HttpStatusCode.BadGateway;
    }

    private static string SafeBody(RawError raw)
    {
        var body = raw.ReadAsString();
        return string.IsNullOrWhiteSpace(body) ? "The billing provider rejected the request." : body;
    }

    private static string DescribeCreateCustomerError(CreateCustomerError error)
    {
        if (error.TryGetCustomerErrorResponse1(out var typed))
        {
            var messages = (typed.Errors?.PerPage ?? Array.Empty<string>())
                .Concat(typed.Errors?.PricePoint ?? Array.Empty<string>())
                .ToList();
            if (messages.Count > 0) return string.Join("; ", messages);
        }
        if (error.TryGetRawError(out var raw))
        {
            var body = raw.ReadAsString();
            if (!string.IsNullOrWhiteSpace(body)) return body;
        }
        return "The billing customer could not be created.";
    }

    private static string DescribeCreateSubscriptionError(CreateSubscriptionError error)
    {
        if (error.TryGetErrorListResponse1(out var typed) && typed.Errors.Count > 0)
            return string.Join("; ", typed.Errors);
        if (error.TryGetRawError(out var raw))
        {
            var body = raw.ReadAsString();
            if (!string.IsNullOrWhiteSpace(body)) return body;
        }
        return "The subscription could not be created.";
    }

    private static string Sanitize(string value) =>
        new(value.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());

    private static string BuildCustomerReference(string userName) => $"eshop-user-{Sanitize(userName)}";

    private static string BuildSubscriptionReference(string userName, string planHandle) => $"eshop-sub-{Sanitize(userName)}-{Sanitize(planHandle)}";

    private async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        try
        {
            return await operation(cts.Token);
        }
        catch (JsonException ex)
        {
            throw new MaxioIntegrationException(HttpStatusCode.BadGateway,
                "The billing provider returned a response that could not be processed.", ex);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new MaxioIntegrationException(HttpStatusCode.GatewayTimeout,
                "The billing provider did not respond in time.");
        }
        catch (HttpRequestException ex)
        {
            throw new MaxioIntegrationException(HttpStatusCode.ServiceUnavailable,
                "The billing provider is currently unreachable.", ex);
        }
    }
}
