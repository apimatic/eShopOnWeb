using System;
using System.Collections.Generic;
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
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public class MaxioBillingService : ISubscriptionBillingService
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private const int ProductPageSize = 200;

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioOptions> options,
        ILogger<MaxioBillingService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken)
    {
        EnsureConfigured();

        var familyHandle = _options.ProductFamilyHandle;
        var productFamilyId = "handle:" + familyHandle;
        var plans = new List<SubscriptionPlan>();

        for (var page = 1; ; page++)
        {
            IReadOnlyList<ProductResponse> envelope;
            try
            {
                envelope = await Bounded(
                    ct => _client.ProductFamilies.ListProductsForProductFamily(
                        productFamilyId: productFamilyId,
                        dateField: null,
                        filter: null,
                        startDate: null,
                        endDate: null,
                        startDatetime: null,
                        endDatetime: null,
                        includeArchived: false,
                        include: null,
                        page: page,
                        perPage: ProductPageSize,
                        ct: ct),
                    cancellationToken);
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                throw TranslateListProductsError(ex);
            }

            foreach (var item in envelope)
            {
                var product = item.Product;
                if (string.IsNullOrWhiteSpace(product.Handle))
                {
                    continue;
                }

                plans.Add(MapPlan(product));
            }

            if (envelope.Count < ProductPageSize)
            {
                break;
            }
        }

        return plans;
    }

    public async Task<ShopperSubscription> SubscribeAsync(
        ShopperIdentity shopper,
        string productHandle,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();

        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new MaxioBillingException(400, "A product handle is required.");
        }

        await EnsureProductInFamilyAsync(productHandle, cancellationToken);

        var customer = await EnsureCustomerAsync(shopper, cancellationToken);
        var subscriptionReference = $"{shopper.UserId}:{productHandle}";

        var existing = await FindSubscriptionOrNullAsync(subscriptionReference, cancellationToken);
        if (existing is not null)
        {
            return MapSubscription(existing);
        }

        try
        {
            using (MaxioWriteGuard.Begin())
            {
                var created = await Bounded(
                    ct => _client.Subscriptions.CreateSubscription(
                        body: new CreateSubscriptionRequest
                        {
                            Subscription = new CreateSubscription
                            {
                                ProductHandle = productHandle,
                                CustomerId = customer.Id,
                                CustomerReference = shopper.UserId,
                                Reference = subscriptionReference
                            }
                        },
                        ct: ct),
                    cancellationToken);

                if (created.Subscription is null)
                {
                    throw new MaxioBillingException(502, "The billing provider returned a response that could not be processed.");
                }

                return MapSubscription(created.Subscription);
            }
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            var raced = await FindSubscriptionOrNullAsync(subscriptionReference, cancellationToken);
            if (raced is not null)
            {
                return MapSubscription(raced);
            }

            throw TranslateCreateSubscriptionError(ex);
        }
        catch (MaxioWriteAlreadySentException)
        {
            var raced = await FindSubscriptionOrNullAsync(subscriptionReference, cancellationToken);
            if (raced is not null)
            {
                return MapSubscription(raced);
            }

            throw new MaxioBillingException(503, "Billing is temporarily unavailable.");
        }
        catch (MaxioBillingException ex) when (ex.StatusCode == 422 || ex.StatusCode >= 500)
        {
            var raced = await FindSubscriptionOrNullAsync(subscriptionReference, cancellationToken);
            if (raced is not null)
            {
                return MapSubscription(raced);
            }

            throw;
        }
    }

    public async Task<IReadOnlyList<ShopperSubscription>> ListMySubscriptionsAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();

        var customer = await ReadCustomerByReferenceOrNullAsync(userId, cancellationToken);
        if (customer?.Id is null)
        {
            return Array.Empty<ShopperSubscription>();
        }

        IReadOnlyList<SubscriptionResponse> envelope;
        try
        {
            envelope = await Bounded(
                ct => _client.Customers.ListCustomerSubscriptions(
                    customerId: customer.Id.Value,
                    ct: ct),
                cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRaw(ex.Error, ex);
        }

        var result = new List<ShopperSubscription>();
        foreach (var item in envelope)
        {
            if (item.Subscription is null)
            {
                continue;
            }

            result.Add(MapSubscription(item.Subscription));
        }

        return result;
    }

    private async Task EnsureProductInFamilyAsync(string productHandle, CancellationToken cancellationToken)
    {
        ProductResponse envelope;
        try
        {
            envelope = await Bounded(
                ct => _client.Products.ReadProductByHandle(apiHandle: productHandle, ct: ct),
                cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            if (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                throw new MaxioBillingException(400, "The selected plan is not available.");
            }

            throw TranslateRaw(ex.Error, ex);
        }

        var familyHandle = envelope.Product.ProductFamily?.Handle;
        if (!string.IsNullOrWhiteSpace(familyHandle)
            && !string.Equals(familyHandle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new MaxioBillingException(400, "The selected plan is not available.");
        }
    }

    private async Task<Customer> EnsureCustomerAsync(ShopperIdentity shopper, CancellationToken cancellationToken)
    {
        var existing = await ReadCustomerByReferenceOrNullAsync(shopper.UserId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            using (MaxioWriteGuard.Begin())
            {
                var created = await Bounded(
                    ct => _client.Customers.CreateCustomer(
                        body: new CreateCustomerRequest
                        {
                            Customer = new CreateCustomer
                            {
                                FirstName = shopper.FirstName,
                                LastName = shopper.LastName,
                                Email = shopper.Email,
                                Reference = shopper.UserId
                            }
                        },
                        ct: ct),
                    cancellationToken);

                return created.Customer;
            }
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            var raced = await ReadCustomerByReferenceOrNullAsync(shopper.UserId, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw TranslateCreateCustomerError(ex);
        }
        catch (MaxioWriteAlreadySentException)
        {
            var raced = await ReadCustomerByReferenceOrNullAsync(shopper.UserId, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw new MaxioBillingException(503, "Billing is temporarily unavailable.");
        }
        catch (MaxioBillingException ex) when (ex.StatusCode == 422 || ex.StatusCode >= 500)
        {
            var raced = await ReadCustomerByReferenceOrNullAsync(shopper.UserId, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw;
        }
    }

    private async Task<Customer?> ReadCustomerByReferenceOrNullAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var envelope = await Bounded(
                ct => _client.Customers.ReadCustomerByReference(reference: reference, ct: ct),
                cancellationToken);
            return envelope.Customer;
        }
        catch (SdkException<RawError> ex)
        {
            if (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            throw TranslateRaw(ex.Error, ex);
        }
    }

    private async Task<Subscription?> FindSubscriptionOrNullAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var envelope = await Bounded(
                ct => _client.Subscriptions.FindSubscription(reference: reference, ct: ct),
                cancellationToken);
            return envelope.Subscription;
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                return null;
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                if (raw.StatusCode == HttpStatusCode.NotFound)
                {
                    return null;
                }

                throw TranslateRaw(raw, ex);
            }

            throw new MaxioBillingException(502, "The billing provider returned a response that could not be processed.", ex);
        }
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        MaxioLastHttp.Clear();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);

        try
        {
            return await call(cts.Token);
        }
        catch (MaxioBillingException)
        {
            throw;
        }
        catch (JsonException ex)
        {
            throw TranslateJsonException(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            throw TranslateTransport(ex);
        }
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey)
            || string.IsNullOrWhiteSpace(_options.Subdomain)
            || string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw new MaxioBillingException(503, "Billing is not configured.");
        }
    }

    private MaxioBillingException TranslateListProductsError(SdkException<ListProductsForProductFamilyError> ex)
    {
        if (ex.Error.TryGetString(out _))
        {
            _logger.LogWarning("Maxio ListProductsForProductFamily returned 404 for family handle {Handle}", _options.ProductFamilyHandle);
            return new MaxioBillingException(404, "Subscription plans were not found.", ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return TranslateRaw(raw, ex);
        }

        return new MaxioBillingException(502, "The billing provider returned a response that could not be processed.", ex);
    }

    private MaxioBillingException TranslateCreateCustomerError(SdkException<CreateCustomerError> ex)
    {
        if (ex.Error.TryGetCustomerErrorResponse1(out _))
        {
            _logger.LogWarning("Maxio CreateCustomer was rejected");
            return new MaxioBillingException(422, "The billing request was rejected.", ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return TranslateRaw(raw, ex);
        }

        return new MaxioBillingException(502, "The billing provider returned a response that could not be processed.", ex);
    }

    private MaxioBillingException TranslateCreateSubscriptionError(SdkException<CreateSubscriptionError> ex)
    {
        if (ex.Error.TryGetErrorListResponse1(out _))
        {
            _logger.LogWarning("Maxio CreateSubscription was rejected");
            return new MaxioBillingException(422, "The billing request was rejected.", ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return TranslateRaw(raw, ex);
        }

        return new MaxioBillingException(502, "The billing provider returned a response that could not be processed.", ex);
    }

    private MaxioBillingException TranslateRaw(RawError raw, Exception inner)
    {
        var status = (int)raw.StatusCode;
        _logger.LogWarning("Maxio returned HTTP {StatusCode}", status);

        if (status >= 400 && status < 500)
        {
            return new MaxioBillingException(status == 404 ? 404 : 422, "The billing request was rejected.", inner);
        }

        return new MaxioBillingException(503, "Billing is temporarily unavailable.", inner);
    }

    private MaxioBillingException TranslateJsonException(JsonException ex)
    {
        var status = MaxioLastHttp.Last;
        if (status is { } code && (int)code >= 400 && (int)code < 500)
        {
            _logger.LogWarning("Maxio rejected the request with HTTP {StatusCode} but the error body could not be read", (int)code);
            return new MaxioBillingException((int)code, "The billing request was rejected.", ex);
        }

        if (status is { } serverCode && (int)serverCode >= 500)
        {
            return new MaxioBillingException(503, "Billing is temporarily unavailable.", ex);
        }

        _logger.LogWarning(ex, "Maxio returned a response that could not be processed");
        return new MaxioBillingException(502, "The billing provider returned a response that could not be processed.", ex);
    }

    private MaxioBillingException TranslateTransport(Exception ex)
    {
        _logger.LogWarning(ex, "Maxio transport failure");
        return new MaxioBillingException(503, "Billing is temporarily unavailable.", ex);
    }

    private static SubscriptionPlan MapPlan(Product product) =>
        new()
        {
            Handle = product.Handle ?? string.Empty,
            Name = product.Name ?? string.Empty,
            Description = product.Description,
            Price = CentsToAmount(product.PriceInCents),
            Interval = product.Interval ?? 1,
            IntervalUnit = product.IntervalUnit?.Value ?? string.Empty,
            ProductPricePointHandle = product.ProductPricePointHandle
        };

    private static ShopperSubscription MapSubscription(Subscription subscription) =>
        new()
        {
            Id = subscription.Id ?? 0,
            Reference = subscription.Reference,
            ProductHandle = subscription.Product?.Handle,
            ProductName = subscription.Product?.Name,
            Price = CentsToAmount(subscription.ProductPriceInCents),
            Currency = subscription.Currency,
            State = subscription.State?.Value ?? string.Empty,
            NextBillingDate = subscription.NextAssessmentAt,
            Interval = subscription.Product?.Interval,
            IntervalUnit = subscription.Product?.IntervalUnit?.Value
        };

    private static decimal CentsToAmount(long? cents) =>
        cents is null ? 0m : cents.Value / 100m;
}
