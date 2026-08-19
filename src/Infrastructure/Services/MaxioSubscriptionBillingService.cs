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
using Microsoft.eShopWeb.ApplicationCore.Entities.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private const int ProductPageSize = 200;
    private static readonly HashSet<string> InactiveSubscriptionStates = new(StringComparer.OrdinalIgnoreCase)
    {
        SubscriptionState.Canceled.Value,
        SubscriptionState.Expired.Value,
        SubscriptionState.FailedToCreate.Value,
        SubscriptionState.TrialEnded.Value
    };

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly IAppLogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioSettings> settings,
        IAppLogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.ProductFamilyHandle))
        {
            throw new MaxioBillingException(
                StatusCodes.BadRequest,
                "Maxio:ProductFamilyHandle is not configured.");
        }

        var familyId = "handle:" + _settings.ProductFamilyHandle;
        var plans = new List<SubscriptionPlan>();
        var page = 1;

        while (true)
        {
            IReadOnlyList<ProductResponse> pageItems;
            try
            {
                pageItems = await _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: familyId,
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
                    ct: cancellationToken);
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                throw TranslateListProductsError(ex);
            }
            catch (Exception ex) when (IsBoundaryException(ex))
            {
                throw TranslateBoundary(ex, "Unable to list subscription plans.");
            }

            foreach (var item in pageItems)
            {
                var product = item.Product;
                if (product is null || string.IsNullOrWhiteSpace(product.Handle))
                {
                    continue;
                }

                plans.Add(MapPlan(product));
            }

            if (pageItems.Count < ProductPageSize)
            {
                break;
            }

            page++;
        }

        return plans;
    }

    public async Task<ShopperSubscription> SubscribeAsync(
        BillingBuyer buyer,
        string productHandle,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new MaxioBillingException(StatusCodes.BadRequest, "ProductHandle is required.");
        }

        var customer = await EnsureCustomerAsync(buyer, cancellationToken);
        var subscriptionKey = $"{buyer.Reference}:{productHandle}";

        var existing = await FindExistingSubscriptionAsync(
            customer.Id!.Value, productHandle, subscriptionKey, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            var created = await _client.Subscriptions.CreateSubscription(
                body: new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = productHandle,
                        CustomerId = customer.Id,
                        Reference = subscriptionKey,
                        PaymentCollectionMethod = CollectionMethod.Remittance
                    }
                },
                ct: cancellationToken);

            var subscription = created.Subscription
                ?? throw new MaxioBillingException(
                    StatusCodes.BadGateway,
                    "The billing provider returned a response that could not be processed.");

            return MapSubscription(subscription);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            var raced = await FindExistingSubscriptionAsync(
                customer.Id!.Value, productHandle, subscriptionKey, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw TranslateCreateSubscriptionError(ex);
        }
        catch (Exception ex) when (IsBoundaryException(ex))
        {
            var raced = await FindExistingSubscriptionAsync(
                customer.Id!.Value, productHandle, subscriptionKey, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw TranslateBoundary(ex, "Unable to create the subscription.");
        }
    }

    public async Task<IReadOnlyList<ShopperSubscription>> ListMySubscriptionsAsync(
        string buyerReference,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerReference))
        {
            return Array.Empty<ShopperSubscription>();
        }

        var customer = await TryReadCustomerByReferenceAsync(buyerReference, cancellationToken);
        if (customer?.Id is null)
        {
            return Array.Empty<ShopperSubscription>();
        }

        return await ListCustomerSubscriptionsAsync(customer.Id.Value, cancellationToken);
    }

    private async Task<Customer> EnsureCustomerAsync(BillingBuyer buyer, CancellationToken cancellationToken)
    {
        var existing = await TryReadCustomerByReferenceAsync(buyer.Reference, cancellationToken);
        if (existing?.Id is not null)
        {
            return existing;
        }

        try
        {
            var created = await _client.Customers.CreateCustomer(
                body: new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = buyer.FirstName,
                        LastName = buyer.LastName,
                        Email = buyer.Email,
                        Reference = buyer.Reference
                    }
                },
                ct: cancellationToken);

            var customer = created.Customer
                ?? throw new MaxioBillingException(
                    StatusCodes.BadGateway,
                    "The billing provider returned a response that could not be processed.");

            if (customer.Id is null)
            {
                throw new MaxioBillingException(
                    StatusCodes.BadGateway,
                    "The billing provider returned a response that could not be processed.");
            }

            return customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            var raced = await TryReadCustomerByReferenceAsync(buyer.Reference, cancellationToken);
            if (raced?.Id is not null)
            {
                return raced;
            }

            throw TranslateCreateCustomerError(ex);
        }
        catch (Exception ex) when (IsBoundaryException(ex))
        {
            var raced = await TryReadCustomerByReferenceAsync(buyer.Reference, cancellationToken);
            if (raced?.Id is not null)
            {
                return raced;
            }

            throw TranslateBoundary(ex, "Unable to create the billing customer.");
        }
    }

    private async Task<Customer?> TryReadCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference, ct: cancellationToken);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when ((int)ex.Error.StatusCode == StatusCodes.NotFound)
        {
            return null;
        }
        catch (Exception ex) when (IsBoundaryException(ex))
        {
            throw TranslateBoundary(ex, "Unable to look up the billing customer.");
        }
    }

    private async Task<ShopperSubscription?> FindExistingSubscriptionAsync(
        int customerId,
        string productHandle,
        string subscriptionKey,
        CancellationToken cancellationToken)
    {
        var byReference = await TryFindSubscriptionByReferenceAsync(subscriptionKey, cancellationToken);
        if (byReference is not null && IsActive(byReference))
        {
            return byReference;
        }

        var forCustomer = await ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return forCustomer.FirstOrDefault(s =>
            string.Equals(s.ProductHandle, productHandle, StringComparison.OrdinalIgnoreCase)
            && IsActive(s));
    }

    private async Task<ShopperSubscription?> TryFindSubscriptionByReferenceAsync(
        string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.Subscriptions.FindSubscription(reference: reference, ct: cancellationToken);
            return response.Subscription is null ? null : MapSubscription(response.Subscription);
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                return null;
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                if ((int)raw.StatusCode == StatusCodes.NotFound)
                {
                    return null;
                }

                throw TranslateRaw(raw, "Unable to look up the subscription.");
            }

            throw new MaxioBillingException(
                StatusCodes.BadGateway,
                "Unable to look up the subscription.",
                ex);
        }
        catch (Exception ex) when (IsBoundaryException(ex))
        {
            throw TranslateBoundary(ex, "Unable to look up the subscription.");
        }
    }

    private async Task<IReadOnlyList<ShopperSubscription>> ListCustomerSubscriptionsAsync(
        int customerId, CancellationToken cancellationToken)
    {
        try
        {
            var responses = await _client.Customers.ListCustomerSubscriptions(customerId, ct: cancellationToken);
            return responses
                .Select(r => r.Subscription)
                .Where(s => s is not null)
                .Select(s => MapSubscription(s!))
                .ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRaw(ex.Error, "Unable to list subscriptions.");
        }
        catch (Exception ex) when (IsBoundaryException(ex))
        {
            throw TranslateBoundary(ex, "Unable to list subscriptions.");
        }
    }

    private static SubscriptionPlan MapPlan(Product product)
    {
        return new SubscriptionPlan(
            Handle: product.Handle ?? string.Empty,
            Name: product.Name ?? string.Empty,
            Description: product.Description ?? string.Empty,
            Price: CentsToAmount(product.PriceInCents),
            Interval: product.Interval ?? 1,
            IntervalUnit: product.IntervalUnit?.Value ?? string.Empty,
            RequiresCreditCard: product.RequireCreditCard ?? false);
    }

    private static ShopperSubscription MapSubscription(Subscription subscription)
    {
        return new ShopperSubscription(
            Id: subscription.Id ?? 0,
            ProductHandle: subscription.Product?.Handle ?? string.Empty,
            ProductName: subscription.Product?.Name ?? string.Empty,
            State: subscription.State?.Value ?? string.Empty,
            Price: CentsToAmount(subscription.ProductPriceInCents),
            CurrentPeriodEndsAt: subscription.CurrentPeriodEndsAt,
            NextBillingAt: subscription.NextAssessmentAt,
            Reference: subscription.Reference);
    }

    private static decimal CentsToAmount(long? cents) => (cents ?? 0) / 100m;

    private static bool IsActive(ShopperSubscription subscription) =>
        !InactiveSubscriptionStates.Contains(subscription.State);

    private MaxioBillingException TranslateListProductsError(SdkException<ListProductsForProductFamilyError> ex)
    {
        if (ex.Error.TryGetString(out var message))
        {
            _logger.LogWarning("Maxio ListProductsForProductFamily 404: {Message}", message);
            return new MaxioBillingException(
                StatusCodes.NotFound,
                "No subscription plans were found for the configured product family.",
                ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return TranslateRaw(raw, "Unable to list subscription plans.");
        }

        return new MaxioBillingException(StatusCodes.BadGateway, "Unable to list subscription plans.", ex);
    }

    private MaxioBillingException TranslateCreateCustomerError(SdkException<CreateCustomerError> ex)
    {
        if (ex.Error.TryGetCustomerErrorResponse1(out _))
        {
            if (ex.Error.TryGetRawError(out var typedRaw))
            {
                return TranslateRaw(typedRaw, "The billing provider rejected the customer.");
            }

            return new MaxioBillingException(
                StatusCodes.BadRequest,
                "The billing provider rejected the customer.",
                ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return TranslateRaw(raw, "The billing provider rejected the customer.");
        }

        return new MaxioBillingException(StatusCodes.BadRequest, "The billing provider rejected the customer.", ex);
    }

    private MaxioBillingException TranslateCreateSubscriptionError(SdkException<CreateSubscriptionError> ex)
    {
        if (ex.Error.TryGetErrorListResponse1(out var list))
        {
            var detail = list.Errors is { Count: > 0 }
                ? string.Join(" ", list.Errors)
                : "The billing provider rejected the subscription.";
            return new MaxioBillingException(StatusCodes.BadRequest, detail, ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return TranslateRaw(raw, "The billing provider rejected the subscription.");
        }

        return new MaxioBillingException(
            StatusCodes.BadRequest,
            "The billing provider rejected the subscription.",
            ex);
    }

    private MaxioBillingException TranslateRaw(RawError raw, string fallback)
    {
        var status = (int)raw.StatusCode;
        var mapped = MapProviderStatus(status);
        if (mapped >= 500)
        {
            _logger.LogWarning("Maxio HTTP {Status}: {Body}", status, SafeLogBody(raw));
        }

        var message = mapped is StatusCodes.BadRequest
            ? TryReadClientMessage(raw) ?? fallback
            : fallback;

        return new MaxioBillingException(mapped, message);
    }

    private MaxioBillingException TranslateBoundary(Exception ex, string fallback)
    {
        if (ex is MaxioBillingException billing)
        {
            return billing;
        }

        if (ex is JsonException)
        {
            var captured = MaxioStatusCaptureHandler.LastStatusCode;
            if (captured is not null && (int)captured.Value is >= 400 and < 500)
            {
                return new MaxioBillingException(
                    (int)captured.Value == StatusCodes.NotFound ? StatusCodes.NotFound : StatusCodes.BadRequest,
                    fallback,
                    ex);
            }

            return new MaxioBillingException(
                StatusCodes.BadGateway,
                "The billing provider returned a response that could not be processed.",
                ex);
        }

        if (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning("Maxio unreachable: {Message}", ex.Message);
            return new MaxioBillingException(
                StatusCodes.ServiceUnavailable,
                "The billing provider is unreachable.",
                ex);
        }

        return new MaxioBillingException(StatusCodes.BadGateway, fallback, ex);
    }

    private static bool IsBoundaryException(Exception ex) =>
        ex is JsonException or HttpRequestException or TaskCanceledException or MaxioBillingException;

    private static int MapProviderStatus(int status) => status switch
    {
        StatusCodes.BadRequest or StatusCodes.UnprocessableEntity => StatusCodes.BadRequest,
        StatusCodes.NotFound => StatusCodes.NotFound,
        StatusCodes.Conflict => StatusCodes.Conflict,
        StatusCodes.Unauthorized or StatusCodes.Forbidden => StatusCodes.BadGateway,
        >= 500 => StatusCodes.BadGateway,
        >= 400 and < 500 => StatusCodes.BadRequest,
        _ => StatusCodes.BadGateway
    };

    private static string? TryReadClientMessage(RawError raw)
    {
        try
        {
            var body = raw.ReadAsString();
            if (string.IsNullOrWhiteSpace(body) || body.Length > 500)
            {
                return null;
            }

            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("errors", out var errors))
            {
                if (errors.ValueKind == JsonValueKind.Array)
                {
                    var parts = errors.EnumerateArray()
                        .Where(e => e.ValueKind == JsonValueKind.String)
                        .Select(e => e.GetString())
                        .Where(s => !string.IsNullOrWhiteSpace(s));
                    var joined = string.Join(" ", parts!);
                    return string.IsNullOrWhiteSpace(joined) ? null : joined;
                }

                if (errors.ValueKind == JsonValueKind.String)
                {
                    return errors.GetString();
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static string SafeLogBody(RawError raw)
    {
        try
        {
            var body = raw.ReadAsString();
            return body.Length <= 500 ? body : body[..500];
        }
        catch
        {
            return "(unreadable)";
        }
    }

    private static class StatusCodes
    {
        public const int BadRequest = 400;
        public const int Unauthorized = 401;
        public const int Forbidden = 403;
        public const int NotFound = 404;
        public const int Conflict = 409;
        public const int UnprocessableEntity = 422;
        public const int BadGateway = 502;
        public const int ServiceUnavailable = 503;
    }
}
