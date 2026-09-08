using System;
using System.Collections.Concurrent;
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
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private const string CustomerReferencePrefix = "eshop-customer";
    private const string SubscriptionReferencePrefix = "eshop-subscription";
    private const int MaxPageSize = 200;
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionService> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _subscribeGates = new();

    public MaxioSubscriptionService(MaxioAdvancedBillingClient client, MaxioOptions options, ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken)
    {
        EnsureConfigured();
        using var timeout = CreateLinkedCancellation(cancellationToken);
        var ct = timeout.Token;

        var familyId = await ResolveFamilyIdAsync(ct);

        IReadOnlyList<ProductResponse> productResponses;
        string? currency;
        try
        {
            productResponses = await _client.ProductFamilies.ListProductsForProductFamily(
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
                perPage: MaxPageSize,
                ct: ct);

            var siteResponse = await _client.Sites.ReadSite(ct: ct);
            currency = siteResponse.Site?.Currency;
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex.Error, ex);
        }
        catch (Exception ex) when (IsProviderTransportFailure(ex))
        {
            throw TranslateTransport(ex);
        }

        return productResponses
            .Where(r => r.Product is not null)
            .Select(r => ToPlanDto(r.Product!, currency))
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(MaxioCustomerProfile profile, string productHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new SubscriptionRequestException("A product handle is required.");
        }

        if (string.IsNullOrWhiteSpace(profile?.UserName))
        {
            throw new SubscriptionRequestException("A user identity is required.");
        }

        EnsureConfigured();

        var gate = _subscribeGates.GetOrAdd(profile.UserName, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            using var timeout = CreateLinkedCancellation(cancellationToken);
            var ct = timeout.Token;

            var product = await ResolveEligibleProductAsync(productHandle, ct);
            var customer = await EnsureCustomerAsync(profile, ct);

            var subscriptionReference = SubscriptionReference(profile.UserName, productHandle);
            var existing = await TryFindSubscriptionAsync(subscriptionReference, ct);
            if (existing is not null)
            {
                return new SubscribeResult
                {
                    WasAlreadySubscribed = true,
                    Subscription = MapSubscription(existing)
                };
            }

            var created = await CreateSubscriptionAsync(
                CustomerReference(profile.UserName),
                product.Handle!,
                subscriptionReference,
                await ResolveCollectionMethodAsync(ct),
                ct);
            return new SubscribeResult
            {
                WasAlreadySubscribed = false,
                Subscription = MapSubscription(created)
            };
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListSubscriptionsAsync(string userName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new SubscriptionRequestException("A user identity is required.");
        }

        EnsureConfigured();
        using var timeout = CreateLinkedCancellation(cancellationToken);
        var ct = timeout.Token;

        var customer = await TryReadCustomerByReferenceAsync(CustomerReference(userName), ct);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var customerId = customer.Id;
        if (customerId is null)
        {
            throw new MaxioConfigurationException("The billing provider returned a customer without an id.");
        }

        IReadOnlyList<SubscriptionResponse> responses;
        try
        {
            responses = await _client.Customers.ListCustomerSubscriptions(customerId: customerId.Value, ct: ct);
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex.Error, ex);
        }
        catch (Exception ex) when (IsProviderTransportFailure(ex))
        {
            throw TranslateTransport(ex);
        }

        return responses
            .Where(r => r.Subscription is not null)
            .Select(r => MapSubscription(r.Subscription!))
            .ToList();
    }

    private async Task<int> ResolveFamilyIdAsync(CancellationToken ct)
    {
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
            throw Translate(ex.Error, ex);
        }
        catch (Exception ex) when (IsProviderTransportFailure(ex))
        {
            throw TranslateTransport(ex);
        }

        var match = families.FirstOrDefault(f =>
            string.Equals(f.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase)
            && f.ProductFamily?.ArchivedAt is null);

        if (match?.ProductFamily is null || match.ProductFamily.Id is not { } familyId)
        {
            throw new MaxioConfigurationException($"The configured product family '{_options.ProductFamilyHandle}' was not found.");
        }

        return familyId;
    }

    private async Task<CollectionMethod> ResolveCollectionMethodAsync(CancellationToken ct)
    {
        Site? site;
        try
        {
            var siteResponse = await _client.Sites.ReadSite(ct: ct);
            site = siteResponse.Site;
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex.Error, ex);
        }
        catch (Exception ex) when (IsProviderTransportFailure(ex))
        {
            throw TranslateTransport(ex);
        }

        if (site?.RelationshipInvoicingEnabled == false)
        {
            return CollectionMethod.Invoice;
        }

        if (site?.DefaultPaymentCollectionMethod is { } defaultMethod)
        {
            if (Matches(defaultMethod, CollectionMethod.Invoice)) return CollectionMethod.Invoice;
            if (Matches(defaultMethod, CollectionMethod.Remittance)) return CollectionMethod.Remittance;
        }

        return CollectionMethod.Remittance;
    }

    private static bool Matches(string wireValue, CollectionMethod method)
        => string.Equals(wireValue, method.Value, StringComparison.OrdinalIgnoreCase);

    private async Task<Product> ResolveEligibleProductAsync(string productHandle, CancellationToken ct)
    {
        var product = await TryGetProductAsync(productHandle, ct);
        if (product is null)
        {
            throw new SubscriptionPlanNotFoundException(productHandle);
        }

        var familyMatches = string.Equals(product.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase);
        if (!familyMatches || product.ArchivedAt is not null || product.Handle is null)
        {
            throw new SubscriptionPlanNotFoundException(productHandle);
        }

        return product;
    }

    private async Task<Product?> TryGetProductAsync(string productHandle, CancellationToken ct)
    {
        try
        {
            var response = await _client.Products.ReadProductByHandle(apiHandle: productHandle, ct: ct);
            return response.Product;
        }
        catch (SdkException<RawError> ex) when (IsNotFound(ex))
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex.Error, ex);
        }
        catch (Exception ex) when (IsProviderTransportFailure(ex))
        {
            throw TranslateTransport(ex);
        }
    }

    private async Task<Customer?> TryReadCustomerByReferenceAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference: reference, ct: ct);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (IsNotFound(ex))
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex.Error, ex);
        }
        catch (Exception ex) when (IsProviderTransportFailure(ex))
        {
            throw TranslateTransport(ex);
        }
    }

    private async Task<Subscription?> TryFindSubscriptionAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Subscriptions.FindSubscription(reference: reference, ct: ct);
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
                throw Translate(raw, ex);
            }

            throw new SubscriptionRequestException("The billing provider rejected the subscription lookup.", ex);
        }
        catch (Exception ex) when (IsProviderTransportFailure(ex))
        {
            throw TranslateTransport(ex);
        }
    }

    private async Task<Customer> EnsureCustomerAsync(MaxioCustomerProfile profile, CancellationToken ct)
    {
        var customerReference = CustomerReference(profile.UserName);
        var existing = await TryReadCustomerByReferenceAsync(customerReference, ct);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            var body = new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = profile.FirstName,
                    LastName = profile.LastName,
                    Email = profile.Email,
                    Reference = customerReference
                }
            };

            var response = await _client.Customers.CreateCustomer(body: body, ct: ct);
            return response.Customer ?? throw new MaxioConfigurationException("The billing provider returned an empty customer response.");
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                var afterConflict = await TryReadCustomerByReferenceAsync(customerReference, ct);
                if (afterConflict is not null)
                {
                    return afterConflict;
                }

                throw new SubscriptionRequestException("The billing provider rejected the customer details.", ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw Translate(raw, ex);
            }

            throw new SubscriptionRequestException("The billing provider rejected the customer details.", ex);
        }
        catch (Exception ex) when (IsProviderTransportFailure(ex))
        {
            var afterFailure = await TryReadCustomerByReferenceAsync(customerReference, ct);
            if (afterFailure is not null)
            {
                return afterFailure;
            }

            throw TranslateTransport(ex);
        }
    }

    private async Task<Subscription> CreateSubscriptionAsync(
        string customerReference,
        string productHandle,
        string subscriptionReference,
        CollectionMethod collectionMethod,
        CancellationToken ct)
    {
        var body = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = productHandle,
                CustomerReference = customerReference,
                Reference = subscriptionReference,
                PaymentCollectionMethod = collectionMethod
            }
        };

        try
        {
            var response = await _client.Subscriptions.CreateSubscription(body: body, ct: ct);
            return response.Subscription ?? throw new MaxioConfigurationException("The billing provider returned an empty subscription response.");
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                var reconciled = await TryFindSubscriptionAsync(subscriptionReference, ct);
                if (reconciled is not null)
                {
                    return reconciled;
                }

                var reason = errorList.Errors is { Count: > 0 }
                    ? string.Join(" ", errorList.Errors)
                    : "The billing provider rejected the subscription request.";
                throw new SubscriptionRequestException(reason, ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw Translate(raw, ex);
            }

            throw new SubscriptionRequestException("The billing provider rejected the subscription request.", ex);
        }
        catch (Exception ex) when (IsProviderTransportFailure(ex))
        {
            var reconciled = await TryFindSubscriptionAsync(subscriptionReference, ct);
            if (reconciled is not null)
            {
                return reconciled;
            }

            throw TranslateTransport(ex);
        }
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey)
            || string.IsNullOrWhiteSpace(_options.ProductFamilyHandle)
            || (string.IsNullOrWhiteSpace(_options.BaseUrl) && string.IsNullOrWhiteSpace(_options.Subdomain)))
        {
            throw new MaxioConfigurationException("Maxio billing is not configured for this environment.");
        }
    }

    private static string CustomerReference(string userName) => $"{CustomerReferencePrefix}-{userName}";

    private static string SubscriptionReference(string userName, string productHandle) => $"{SubscriptionReferencePrefix}-{userName}-{productHandle}";

    private static CancellationTokenSource CreateLinkedCancellation(CancellationToken callerToken)
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        timeout.CancelAfter(CallBudget);
        return timeout;
    }

    private static bool IsProviderTransportFailure(Exception ex) => ex is HttpRequestException or JsonException or OperationCanceledException;

    private static bool IsNotFound(SdkException<RawError> ex) => ex.Error.StatusCode == HttpStatusCode.NotFound;

    private SubscriptionServiceUnavailableException TranslateTransport(Exception ex)
    {
        var message = ex is OperationCanceledException
            ? "The billing provider request timed out."
            : "The billing provider could not be reached.";
        _logger.LogWarning(ex, "Maxio transport failure: {Message}", message);
        return new SubscriptionServiceUnavailableException(message, ex);
    }

    private Exception Translate(RawError raw, Exception inner)
    {
        LogProviderError(raw);
        var status = raw.StatusCode;
        if (status == HttpStatusCode.Unauthorized || status == HttpStatusCode.Forbidden)
        {
            return new MaxioConfigurationException("The billing provider rejected the configured API credentials.", inner);
        }

        if (status == HttpStatusCode.TooManyRequests || (int)status >= 500)
        {
            return new SubscriptionServiceUnavailableException("The billing provider is temporarily unavailable.", inner);
        }

        return new SubscriptionRequestException("The billing provider rejected the request.", inner);
    }

    private void LogProviderError(RawError raw)
    {
        var statusCode = (int)raw.StatusCode;
        try
        {
            var body = raw.ReadAsString();
            if (body.Length > 1000)
            {
                body = body[..1000];
            }

            _logger.LogWarning("Maxio API error {StatusCode}: {Body}", statusCode, body);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Maxio API error {StatusCode} (response body unreadable)", statusCode);
        }
    }

    private static SubscriptionPlanDto ToPlanDto(Product product, string? currency)
    {
        return new SubscriptionPlanDto
        {
            Handle = product.Handle ?? string.Empty,
            Name = product.Name ?? string.Empty,
            Price = ToDollars(product.PriceInCents),
            Currency = currency ?? string.Empty,
            Interval = product.Interval ?? 1,
            IntervalUnit = product.IntervalUnit?.Value ?? string.Empty
        };
    }

    private static SubscriptionDto MapSubscription(Subscription subscription)
    {
        return new SubscriptionDto
        {
            Id = subscription.Id ?? 0,
            Reference = subscription.Reference ?? string.Empty,
            PlanHandle = subscription.Product?.Handle ?? string.Empty,
            PlanName = subscription.Product?.Name ?? string.Empty,
            PricePointName = subscription.Product?.ProductPricePointName ?? string.Empty,
            Price = ToDollars(subscription.ProductPriceInCents),
            Currency = subscription.Currency ?? string.Empty,
            State = subscription.State?.Value ?? string.Empty,
            NextBillingDate = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt
        };
    }

    private static decimal ToDollars(long? cents) => cents.HasValue ? Math.Round(cents.Value / 100m, 2) : 0m;
}
