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
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionBilling;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioOptions> options,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = new List<Product>();
        var page = 1;
        const int perPage = 20;

        while (true)
        {
            var batch = await Execute(
                ct => _client.Products.ListProducts(
                    dateField: null,
                    filter: null,
                    endDate: null,
                    endDatetime: null,
                    startDate: null,
                    startDatetime: null,
                    includeArchived: false,
                    include: null,
                    page: page,
                    perPage: perPage,
                    ct: ct),
                cancellationToken);

            foreach (var item in batch)
            {
                if (item.Product is not null)
                {
                    products.Add(item.Product);
                }
            }

            if (batch.Count < perPage)
            {
                break;
            }

            page++;
        }

        IEnumerable<Product> inFamily = products;
        if (!string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            var matching = products
                .Where(p => string.Equals(p.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matching.Count > 0)
            {
                inFamily = matching;
            }
            else
            {
                _logger.LogWarning(
                    "No Maxio products matched product family handle {FamilyHandle}; returning unfiltered catalog.",
                    _options.ProductFamilyHandle);
            }
        }

        return inFamily
            .Where(p => !string.IsNullOrWhiteSpace(p.Handle))
            .Select(MapPlan)
            .ToList();
    }

    public async Task<SubscriptionDetails> SubscribeAsync(SubscribeShopperRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            throw new BillingException("A productHandle is required.", 400);
        }

        await ReadProductInFamily(request.ProductHandle, cancellationToken);
        var customer = await EnsureCustomerAsync(request, cancellationToken);
        var subscriptionKey = BuildSubscriptionReference(request.ShopperUserId, request.ProductHandle);

        var existing = await TryFindSubscriptionAsync(subscriptionKey, cancellationToken);
        if (existing is not null && IsOpenSubscription(existing))
        {
            return MapSubscription(existing, alreadyExisted: true);
        }

        if (customer.Id is not null)
        {
            var alreadyOnPlan = await TryFindOpenSubscriptionForProductAsync(customer.Id.Value, request.ProductHandle, cancellationToken);
            if (alreadyOnPlan is not null)
            {
                return MapSubscription(alreadyOnPlan, alreadyExisted: true);
            }
        }

        if (customer.Id is null)
        {
            throw new BillingException("The billing customer could not be identified.", 502);
        }

        try
        {
            using var writeScope = WriteOnceScope.Begin();
            var created = await Execute(
                ct => _client.Subscriptions.CreateSubscription(
                    body: new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            ProductHandle = request.ProductHandle,
                            CustomerId = customer.Id,
                            Reference = subscriptionKey,
                            PaymentCollectionMethod = CollectionMethod.Invoice
                        }
                    },
                    ct: ct),
                cancellationToken);

            if (created.Subscription is null)
            {
                throw new BillingException("The billing provider returned a response that could not be processed.", 502);
            }

            return MapSubscription(created.Subscription, alreadyExisted: false);
        }
        catch (BillingException ex) when (ex.StatusCode is 409 or 422)
        {
            var recovered = await TryFindSubscriptionAsync(subscriptionKey, cancellationToken);
            if (recovered is not null)
            {
                return MapSubscription(recovered, alreadyExisted: true);
            }

            throw;
        }
        catch (DuplicateWriteRejectedException)
        {
            var recovered = await TryFindSubscriptionAsync(subscriptionKey, cancellationToken);
            if (recovered is not null)
            {
                return MapSubscription(recovered, alreadyExisted: true);
            }

            throw new BillingException("The subscribe request may already have been submitted. Please retry.", 409);
        }
    }

    public async Task<IReadOnlyList<SubscriptionDetails>> ListMySubscriptionsAsync(string shopperUserId, CancellationToken cancellationToken = default)
    {
        var customer = await TryReadCustomerByReferenceAsync(shopperUserId, cancellationToken);
        if (customer?.Id is null)
        {
            return Array.Empty<SubscriptionDetails>();
        }

        var list = await Execute(
            ct => _client.Customers.ListCustomerSubscriptions(customerId: customer.Id.Value, ct: ct),
            cancellationToken);

        return list
            .Where(item => item.Subscription is not null)
            .Select(item => MapSubscription(item.Subscription!, alreadyExisted: false))
            .ToList();
    }

    private async Task<Product> ReadProductInFamily(string productHandle, CancellationToken cancellationToken)
    {
        ProductResponse response;
        try
        {
            response = await Execute(
                ct => _client.Products.ReadProductByHandle(apiHandle: productHandle, ct: ct),
                cancellationToken);
        }
        catch (BillingException ex) when (ex.StatusCode == 404)
        {
            throw new BillingException($"Unknown subscription plan '{productHandle}'.", 400, ex);
        }

        var product = response.Product;
        if (!string.IsNullOrWhiteSpace(_options.ProductFamilyHandle)
            && product.ProductFamily?.Handle is string familyHandle
            && !string.Equals(familyHandle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new BillingException($"Unknown subscription plan '{productHandle}'.", 400);
        }

        return product;
    }

    private async Task<Customer> EnsureCustomerAsync(SubscribeShopperRequest request, CancellationToken cancellationToken)
    {
        var existing = await TryReadCustomerByReferenceAsync(request.ShopperUserId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            using var writeScope = WriteOnceScope.Begin();
            var created = await Execute(
                ct => _client.Customers.CreateCustomer(
                    body: new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer
                        {
                            FirstName = string.IsNullOrWhiteSpace(request.FirstName) ? "Shopper" : request.FirstName,
                            LastName = string.IsNullOrWhiteSpace(request.LastName) ? "eShopOnWeb" : request.LastName,
                            Email = request.Email,
                            Reference = request.ShopperUserId
                        }
                    },
                    ct: ct),
                cancellationToken);

            return created.Customer;
        }
        catch (BillingException ex) when (ex.StatusCode is 409 or 422)
        {
            var recovered = await RecoverCustomerAsync(request, cancellationToken);
            if (recovered is not null)
            {
                return recovered;
            }

            throw;
        }
        catch (DuplicateWriteRejectedException)
        {
            var recovered = await RecoverCustomerAsync(request, cancellationToken);
            if (recovered is not null)
            {
                return recovered;
            }

            throw new BillingException("The billing customer request may already have been submitted. Please retry.", 409);
        }
    }

    private async Task<Customer?> RecoverCustomerAsync(SubscribeShopperRequest request, CancellationToken cancellationToken)
    {
        var byReference = await TryReadCustomerByReferenceAsync(request.ShopperUserId, cancellationToken);
        if (byReference is not null)
        {
            return byReference;
        }

        var byEmail = await TryFindCustomerByEmailAsync(request.Email, cancellationToken);
        if (byEmail?.Id is null)
        {
            return null;
        }

        return await RelinkCustomerReferenceAsync(byEmail, request.ShopperUserId, cancellationToken);
    }

    private async Task<Customer?> TryFindCustomerByEmailAsync(string email, CancellationToken cancellationToken)
    {
        var list = await Execute(
            ct => _client.Customers.ListCustomers(
                direction: null,
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                q: email,
                page: 1,
                perPage: 50,
                ct: ct),
            cancellationToken);

        return list
            .Select(item => item.Customer)
            .FirstOrDefault(customer =>
                string.Equals(customer.Email, email, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<Customer> RelinkCustomerReferenceAsync(Customer customer, string shopperUserId, CancellationToken cancellationToken)
    {
        if (string.Equals(customer.Reference, shopperUserId, StringComparison.Ordinal))
        {
            return customer;
        }

        if (customer.Id is null)
        {
            return customer;
        }

        var updated = await Execute(
            ct => _client.Customers.UpdateCustomer(
                id: customer.Id.Value,
                body: new UpdateCustomerRequest
                {
                    Customer = new UpdateCustomer
                    {
                        Reference = shopperUserId
                    }
                },
                ct: ct),
            cancellationToken);

        return updated.Customer;
    }

    private async Task<Customer?> TryReadCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Execute(
                ct => _client.Customers.ReadCustomerByReference(reference: reference, ct: ct),
                cancellationToken);
            return response.Customer;
        }
        catch (BillingException ex) when (ex.StatusCode == 404)
        {
            return null;
        }
    }

    private async Task<Subscription?> TryFindSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Execute(
                ct => _client.Subscriptions.FindSubscription(reference: reference, ct: ct),
                cancellationToken);
            return response.Subscription;
        }
        catch (BillingException ex) when (ex.StatusCode == 404)
        {
            return null;
        }
    }

    private async Task<Subscription?> TryFindOpenSubscriptionForProductAsync(int customerId, string productHandle, CancellationToken cancellationToken)
    {
        var list = await Execute(
            ct => _client.Customers.ListCustomerSubscriptions(customerId: customerId, ct: ct),
            cancellationToken);

        return list
            .Select(item => item.Subscription)
            .FirstOrDefault(subscription =>
                subscription is not null
                && IsOpenSubscription(subscription)
                && string.Equals(subscription.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<T> Execute<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        HttpStatusCaptureHandler.Clear();

        try
        {
            return await Bounded(call, cancellationToken);
        }
        catch (BillingException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw Translate(ex);
        }
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private BillingException Translate(Exception ex) => ex switch
    {
        BillingException billing => billing,
        DuplicateWriteRejectedException duplicate => new BillingException(
            "The billing request may already have been submitted. Please retry.", 409, duplicate),
        SdkException<RawError> raw => MapRaw(raw),
        SdkException<CreateCustomerError> createCustomer => MapCreateCustomer(createCustomer),
        SdkException<UpdateCustomerError> updateCustomer => MapUpdateCustomer(updateCustomer),
        SdkException<CreateSubscriptionError> createSubscription => MapCreateSubscription(createSubscription),
        SdkException<FindSubscriptionError> findSubscription => MapFindSubscription(findSubscription),
        JsonException json => MapJson(json),
        HttpRequestException http => new BillingException("The billing provider is unreachable.", 503, http),
        TaskCanceledException canceled => new BillingException("The billing request timed out.", 504, canceled),
        _ => new BillingException("Unexpected billing failure.", 502, ex)
    };

    private static BillingException MapRaw(SdkException<RawError> ex)
    {
        var status = MapProviderStatus(ex.Error.StatusCode);
        var message = status switch
        {
            404 => "The requested billing resource was not found.",
            401 or 403 => "The billing provider rejected the request.",
            >= 400 and < 500 => "The billing request was rejected.",
            _ => "The billing provider is unavailable."
        };
        return new BillingException(message, status, ex);
    }

    private static BillingException MapCreateCustomer(SdkException<CreateCustomerError> ex)
    {
        if (ex.Error.TryGetCustomerErrorResponse1(out _))
        {
            return new BillingException("The customer could not be created.", 422, ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return new BillingException("The customer could not be created.", MapProviderStatus(raw.StatusCode), ex);
        }

        return new BillingException("The customer could not be created.", 422, ex);
    }

    private static BillingException MapUpdateCustomer(SdkException<UpdateCustomerError> ex)
    {
        if (ex.Error.TryGetNoContent(out _))
        {
            return new BillingException("The requested billing resource was not found.", 404, ex);
        }

        if (ex.Error.TryGetCustomerErrorResponse1(out _))
        {
            return new BillingException("The customer could not be updated.", 422, ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return new BillingException("The customer could not be updated.", MapProviderStatus(raw.StatusCode), ex);
        }

        return new BillingException("The customer could not be updated.", 422, ex);
    }

    private static BillingException MapCreateSubscription(SdkException<CreateSubscriptionError> ex)
    {
        if (ex.Error.TryGetErrorListResponse1(out var body))
        {
            var message = FormatErrorList(body.Errors) ?? "The subscription could not be created.";
            return new BillingException(message, 422, ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return new BillingException("The subscription could not be created.", MapProviderStatus(raw.StatusCode), ex);
        }

        return new BillingException("The subscription could not be created.", 422, ex);
    }

    private static BillingException MapFindSubscription(SdkException<FindSubscriptionError> ex)
    {
        if (ex.Error.TryGetNoContent(out _))
        {
            return new BillingException("The requested billing resource was not found.", 404, ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return new BillingException("The requested billing resource was not found.", MapProviderStatus(raw.StatusCode), ex);
        }

        return new BillingException("The requested billing resource was not found.", 404, ex);
    }

    private static BillingException MapJson(JsonException ex)
    {
        var captured = HttpStatusCaptureHandler.Current;
        if (captured is HttpStatusCode status && (int)status is >= 400 and < 500)
        {
            return new BillingException("The billing request was rejected.", (int)status, ex);
        }

        return new BillingException("The billing provider returned a response that could not be processed.", 502, ex);
    }

    private static int MapProviderStatus(HttpStatusCode status)
    {
        var code = (int)status;
        if (code is 401 or 403)
        {
            return 502;
        }

        if (code is >= 400 and < 500)
        {
            return code;
        }

        return 502;
    }

    private static string? FormatErrorList(IReadOnlyList<string>? errors)
    {
        if (errors is null || errors.Count == 0)
        {
            return null;
        }

        var actionLink = errors.FirstOrDefault(e =>
            e.Contains("action_link", StringComparison.OrdinalIgnoreCase)
            || e.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || e.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(actionLink))
        {
            return actionLink;
        }

        return string.Join(" ", errors.Where(e => !string.IsNullOrWhiteSpace(e)));
    }

    private static bool IsOpenSubscription(Subscription subscription)
    {
        var state = subscription.State;
        if (state is null)
        {
            return true;
        }

        return state != SubscriptionState.Canceled
            && state != SubscriptionState.Expired
            && state != SubscriptionState.FailedToCreate;
    }

    private static string BuildSubscriptionReference(string shopperUserId, string productHandle) =>
        $"{shopperUserId}:{productHandle}";

    private static SubscriptionPlan MapPlan(Product product) => new()
    {
        ProductId = product.Id,
        Handle = product.Handle!,
        Name = product.Name ?? product.Handle!,
        Description = product.Description,
        Price = FromCents(product.PriceInCents),
        Interval = product.Interval ?? 1,
        IntervalUnit = product.IntervalUnit?.Value,
        RequireCreditCard = product.RequireCreditCard ?? false
    };

    private static SubscriptionDetails MapSubscription(Subscription subscription, bool alreadyExisted) => new()
    {
        Id = subscription.Id ?? 0,
        Reference = subscription.Reference,
        State = subscription.State?.Value,
        ProductHandle = subscription.Product?.Handle,
        ProductName = subscription.Product?.Name,
        Price = FromCents(subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents),
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingDate = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
        AlreadyExisted = alreadyExisted
    };

    private static decimal FromCents(long? cents) =>
        cents is null ? 0m : cents.Value / 100m;
}
