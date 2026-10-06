using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private const string CustomerReferencePrefix = "eshop";

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionService> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioOptions> options,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken ct)
    {
        var family = await FindProductFamilyAsync(ct);
        if (family == null)
        {
            throw new MaxioException(
                $"The configured Maxio product family '{_options.ProductFamilyHandle}' was not found.",
                HttpStatusCode.NotFound);
        }

        var products = await CallAsync(() => _client.ProductFamilies.ListProductsForProductFamily(
            productFamilyId: family.Id?.ToString() ?? string.Empty,
            dateField: null,
            filter: null,
            startDate: null,
            endDate: null,
            startDatetime: null,
            endDatetime: null,
            includeArchived: null,
            include: null,
            page: 1,
            perPage: 100,
            ct: ct), ct);

        var plans = new List<SubscriptionPlanDto>();
        foreach (var productResponse in products)
        {
            var product = productResponse.Product;
            if (product == null || product.ArchivedAt != null)
            {
                continue;
            }

            plans.Add(new SubscriptionPlanDto
            {
                Id = product.Id ?? 0,
                Handle = product.Handle ?? string.Empty,
                Name = product.Name ?? string.Empty,
                Price = CentsToDollars(product.PriceInCents),
                Interval = product.Interval ?? 0,
                IntervalUnit = product.IntervalUnit?.Value ?? string.Empty,
                RequireCreditCard = product.RequireCreditCard ?? false
            });
        }

        return plans;
    }

    public async Task<SubscriptionDto> SubscribeAsync(string username, string planHandle, CancellationToken ct)
    {
        var customerReference = CustomerReferenceFor(username);
        var subscriptionReference = SubscriptionReferenceFor(username, planHandle);

        var semaphore = _locks.GetOrAdd(customerReference, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(ct);
        try
        {
            var customer = await FindOrCreateCustomerAsync(customerReference, username, ct);
            await ReadProductByHandleAsync(planHandle, ct);

            var existing = await TryFindSubscriptionAsync(subscriptionReference, ct);
            if (existing != null)
            {
                _logger.LogInformation("Subscription {SubscriptionReference} already exists; returning it.", subscriptionReference);
                return MapSubscription(existing);
            }

            try
            {
                var created = await CallAsync(() => _client.Subscriptions.CreateSubscription(
                    new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            ProductHandle = planHandle,
                            CustomerId = customer.Id,
                            Reference = subscriptionReference,
                            PaymentCollectionMethod = CollectionMethod.Remittance
                        }
                    }, ct: ct), ct);
                if (created.Subscription == null)
                {
                    throw new MaxioException("Maxio did not return the created subscription.", HttpStatusCode.BadGateway);
                }
                return MapSubscription(created.Subscription);
            }
            catch (SdkException<CreateSubscriptionError> ex)
            {
                var raced = await TryFindSubscriptionAsync(subscriptionReference, ct);
                if (raced != null)
                {
                    return MapSubscription(raced);
                }

                if (ex.Error.TryGetErrorListResponse1(out _))
                {
                    throw new MaxioException("Maxio rejected the subscription creation.", HttpStatusCode.UnprocessableEntity, ex);
                }
                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw new MaxioException("Maxio rejected the subscription creation.", raw.StatusCode, ex);
                }
                throw new MaxioException("Maxio rejected the subscription creation.", HttpStatusCode.BadGateway, ex);
            }
            catch (MaxioException ex) when (ex.StatusCode >= HttpStatusCode.InternalServerError)
            {
                var reconciled = await TryFindSubscriptionAsync(subscriptionReference, ct);
                if (reconciled != null)
                {
                    return MapSubscription(reconciled);
                }
                throw;
            }
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetSubscriptionsAsync(string username, CancellationToken ct)
    {
        var customerReference = CustomerReferenceFor(username);
        var customer = await TryReadCustomerByReferenceAsync(customerReference, ct);
        if (customer == null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var subscriptions = await CallAsync(() => _client.Customers.ListCustomerSubscriptions(customer.Id ?? 0, ct: ct), ct);

        var result = new List<SubscriptionDto>();
        foreach (var subscriptionResponse in subscriptions)
        {
            var subscription = subscriptionResponse.Subscription;
            if (subscription == null)
            {
                continue;
            }
            result.Add(MapSubscription(subscription));
        }
        return result;
    }

    private async Task<ProductFamily?> FindProductFamilyAsync(CancellationToken ct)
    {
        var families = await CallAsync(() => _client.ProductFamilies.ListProductFamilies(
            dateField: null,
            startDate: null,
            endDate: null,
            startDatetime: null,
            endDatetime: null,
            ct: ct), ct);

        foreach (var familyResponse in families)
        {
            var family = familyResponse.ProductFamily;
            if (family != null &&
                string.Equals(family.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
            {
                return family;
            }
        }
        return null;
    }

    private async Task<Product> ReadProductByHandleAsync(string planHandle, CancellationToken ct)
    {
        try
        {
            var response = await CallAsync(() => _client.Products.ReadProductByHandle(planHandle, ct: ct), ct);
            if (response.Product == null)
            {
                throw new MaxioException($"The plan '{planHandle}' was not found.", HttpStatusCode.NotFound);
            }
            return response.Product;
        }
        catch (MaxioException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new MaxioException($"The plan '{planHandle}' was not found.", HttpStatusCode.NotFound, ex);
        }
    }

    private async Task<Customer> FindOrCreateCustomerAsync(string customerReference, string email, CancellationToken ct)
    {
        var existing = await TryReadCustomerByReferenceAsync(customerReference, ct);
        if (existing != null)
        {
            return existing;
        }

        try
        {
            var response = await CallAsync(() => _client.Customers.CreateCustomer(
                new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = email,
                        LastName = email,
                        Email = email,
                        Reference = customerReference
                    }
                }, ct: ct), ct);
            if (response.Customer == null)
            {
                throw new MaxioException("Maxio did not return the created customer.", HttpStatusCode.BadGateway);
            }
            return response.Customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            var raced = await TryReadCustomerByReferenceAsync(customerReference, ct);
            if (raced != null)
            {
                return raced;
            }

            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                throw new MaxioException("Maxio rejected the customer creation.", HttpStatusCode.UnprocessableEntity, ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new MaxioException("Maxio rejected the customer creation.", raw.StatusCode, ex);
            }
            throw new MaxioException("Maxio rejected the customer creation.", HttpStatusCode.BadGateway, ex);
        }
        catch (MaxioException ex) when (ex.StatusCode >= HttpStatusCode.InternalServerError)
        {
            var raced = await TryReadCustomerByReferenceAsync(customerReference, ct);
            if (raced != null)
            {
                return raced;
            }
            throw;
        }
    }

    private async Task<Customer?> TryReadCustomerByReferenceAsync(string customerReference, CancellationToken ct)
    {
        try
        {
            var response = await CallAsync(() => _client.Customers.ReadCustomerByReference(customerReference, ct: ct), ct);
            return response.Customer;
        }
        catch (MaxioException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<Subscription?> TryFindSubscriptionAsync(string subscriptionReference, CancellationToken ct)
    {
        try
        {
            var response = await CallAsync(() => _client.Subscriptions.FindSubscription(reference: subscriptionReference, ct: ct), ct);
            return response.Subscription;
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                return null;
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new MaxioException("Maxio request failed.", raw.StatusCode, ex);
            }
            throw new MaxioException("Maxio request failed.", HttpStatusCode.BadGateway, ex);
        }
        catch (MaxioException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<T> CallAsync<T>(Func<Task<T>> call, CancellationToken ct)
    {
        try
        {
            return await call();
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Maxio request failed with HTTP {StatusCode}.", (int)ex.Error.StatusCode);
            throw new MaxioException("Maxio request failed.", ex.Error.StatusCode, ex);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Maxio returned a response that could not be processed.");
            throw new MaxioException("Maxio returned a response that could not be processed.", HttpStatusCode.BadGateway, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogError(ex, "Maxio is unreachable.");
            throw new MaxioException("Maxio is unreachable.", HttpStatusCode.BadGateway, ex);
        }
    }

    private static SubscriptionDto MapSubscription(Subscription subscription)
    {
        return new SubscriptionDto
        {
            Id = subscription.Id ?? 0,
            Reference = subscription.Reference ?? string.Empty,
            State = subscription.State?.Value ?? string.Empty,
            PlanHandle = subscription.Product?.Handle ?? string.Empty,
            PlanName = subscription.Product?.Name ?? string.Empty,
            Price = CentsToDollars(subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents),
            NextBillingDate = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt
        };
    }

    private static decimal CentsToDollars(long? cents) => cents.HasValue ? cents.Value / 100m : 0m;

    private static string CustomerReferenceFor(string username) => $"{CustomerReferencePrefix}-{username}";

    private static string SubscriptionReferenceFor(string username, string planHandle) => $"{CustomerReferenceFor(username)}-{planHandle}";
}
