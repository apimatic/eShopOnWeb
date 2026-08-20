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
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private const int ProductPageSize = 20;
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled",
        "expired",
        "failed_to_create",
        "trial_ended"
    };

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly IAppLogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioOptions> options,
        IAppLogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var plans = new List<SubscriptionPlan>();
        var page = 1;
        while (true)
        {
            var familyId = $"handle:{_options.ProductFamilyHandle}";
            IReadOnlyList<ProductResponse> pageItems;
            try
            {
                pageItems = await Invoke(
                    ct => _client.ProductFamilies.ListProductsForProductFamily(
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
                        ct: ct),
                    cancellationToken,
                    catchTyped: async (ct, inner) =>
                    {
                        try
                        {
                            return await inner(ct);
                        }
                        catch (SdkException<ListProductsForProductFamilyError> ex)
                        {
                            throw MapListProductsError(ex);
                        }
                    });
            }
            catch (BillingException)
            {
                throw;
            }

            foreach (var item in pageItems)
            {
                var product = item.Product;
                if (string.IsNullOrWhiteSpace(product.Handle) || string.IsNullOrWhiteSpace(product.Name))
                {
                    continue;
                }

                plans.Add(ToPlan(product));
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
        string buyerId,
        string email,
        string? displayName,
        string productHandle,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new BillingException(400, "A product handle is required.");
        }

        productHandle = productHandle.Trim();
        var product = await ReadFamilyProductAsync(productHandle, cancellationToken);
        var customerId = await EnsureCustomerIdAsync(buyerId, email, displayName, cancellationToken);

        var existing = await FindOpenSubscriptionAsync(customerId, productHandle, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation("Returning existing Maxio subscription {SubscriptionId} for buyer {BuyerId}", existing.Id, buyerId);
            return existing;
        }

        var reference = SubscriptionReference(buyerId, productHandle);
        var body = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = productHandle,
                CustomerId = customerId,
                Reference = reference,
                PaymentCollectionMethod = CollectionMethod.Remittance
            }
        };

        try
        {
            using (MaxioWriteOnce.Arm())
            {
                var created = await Invoke(
                    ct => _client.Subscriptions.CreateSubscription(body, ct: ct),
                    cancellationToken,
                    catchTyped: async (ct, inner) =>
                    {
                        try
                        {
                            return await inner(ct);
                        }
                        catch (SdkException<CreateSubscriptionError> ex)
                        {
                            throw MapCreateSubscriptionError(ex);
                        }
                    });

                return MapSubscription(created.Subscription, product);
            }
        }
        catch (BillingException ex) when (ex.StatusCode is 502 or 504)
        {
            var recovered = await TryFindSubscriptionByReferenceAsync(reference, product, cancellationToken);
            if (recovered is not null)
            {
                return recovered;
            }

            throw;
        }
    }

    public async Task<IReadOnlyList<ShopperSubscription>> ListSubscriptionsAsync(
        string buyerId,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var customerId = await TryReadCustomerByReferenceAsync(buyerId, cancellationToken);
        if (customerId is null)
        {
            return Array.Empty<ShopperSubscription>();
        }

        var responses = await Invoke(
            ct => _client.Customers.ListCustomerSubscriptions(customerId.Value, ct: ct),
            cancellationToken);

        return responses
            .Select(item => item.Subscription)
            .Where(subscription => subscription is not null)
            .Select(subscription => MapSubscription(subscription, product: null))
            .ToList();
    }

    private async Task<Product> ReadFamilyProductAsync(string productHandle, CancellationToken cancellationToken)
    {
        ProductResponse response;
        try
        {
            response = await Invoke(
                ct => _client.Products.ReadProductByHandle(productHandle, ct: ct),
                cancellationToken);
        }
        catch (BillingException ex) when (ex.StatusCode == 404)
        {
            throw new BillingException(404, "The selected plan was not found.", ex);
        }

        var product = response.Product;
        var familyHandle = product.ProductFamily?.Handle;
        if (!string.Equals(familyHandle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new BillingException(400, "The selected plan is not available.");
        }

        return product;
    }

    private async Task<int> EnsureCustomerIdAsync(
        string buyerId,
        string email,
        string? displayName,
        CancellationToken cancellationToken)
    {
        var existing = await TryReadCustomerByReferenceAsync(buyerId, cancellationToken);
        if (existing is not null)
        {
            return existing.Value;
        }

        var (firstName, lastName) = SplitName(displayName, email);
        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = buyerId
            }
        };

        try
        {
            using (MaxioWriteOnce.Arm())
            {
                var created = await Invoke(
                    ct => _client.Customers.CreateCustomer(body, ct: ct),
                    cancellationToken,
                    catchTyped: async (ct, inner) =>
                    {
                        try
                        {
                            return await inner(ct);
                        }
                        catch (SdkException<CreateCustomerError> ex)
                        {
                            throw MapCreateCustomerError(ex);
                        }
                    });

                return RequireId(created.Customer.Id, "customer");
            }
        }
        catch (BillingException ex) when (ex.StatusCode is 422 or 502 or 504)
        {
            var recovered = await TryReadCustomerByReferenceAsync(buyerId, cancellationToken);
            if (recovered is not null)
            {
                return recovered.Value;
            }

            throw;
        }
    }

    private async Task<int?> TryReadCustomerByReferenceAsync(string buyerId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Invoke(
                ct => _client.Customers.ReadCustomerByReference(buyerId, ct: ct),
                cancellationToken);
            return RequireId(response.Customer.Id, "customer");
        }
        catch (BillingException ex) when (ex.StatusCode == 404)
        {
            return null;
        }
    }

    private async Task<ShopperSubscription?> FindOpenSubscriptionAsync(
        int customerId,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var responses = await Invoke(
            ct => _client.Customers.ListCustomerSubscriptions(customerId, ct: ct),
            cancellationToken);

        foreach (var item in responses)
        {
            var subscription = item.Subscription;
            if (subscription is null)
            {
                continue;
            }

            var handle = subscription.Product?.Handle;
            if (!string.Equals(handle, productHandle, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (IsOpen(subscription.State?.Value))
            {
                return MapSubscription(subscription, subscription.Product);
            }
        }

        return null;
    }

    private async Task<ShopperSubscription?> TryFindSubscriptionByReferenceAsync(
        string reference,
        Product product,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await Invoke(
                ct => _client.Subscriptions.FindSubscription(reference: reference, ct: ct),
                cancellationToken,
                catchTyped: async (ct, inner) =>
                {
                    try
                    {
                        return await inner(ct);
                    }
                    catch (SdkException<FindSubscriptionError> ex)
                    {
                        throw MapFindSubscriptionError(ex);
                    }
                });

            return MapSubscription(response.Subscription, product);
        }
        catch (BillingException ex) when (ex.StatusCode == 404)
        {
            return null;
        }
    }

    private async Task<T> Invoke<T>(
        Func<CancellationToken, Task<T>> call,
        CancellationToken cancellationToken,
        Func<CancellationToken, Func<CancellationToken, Task<T>>, Task<T>>? catchTyped = null)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(MaxioBillingClientFactory.CallBudget);
        MaxioLastHttp.Status.Value = null;

        try
        {
            if (catchTyped is not null)
            {
                return await catchTyped(cts.Token, call);
            }

            return await call(cts.Token);
        }
        catch (BillingException)
        {
            throw;
        }
        catch (MaxioDuplicateSendException ex)
        {
            throw new BillingException(502, "The billing request may have already been processed. Please retry.", ex);
        }
        catch (JsonException ex)
        {
            throw MapJsonException(ex);
        }
        catch (HttpRequestException ex)
        {
            throw new BillingException(502, "The billing provider is unreachable.", ex);
        }
        catch (TaskCanceledException ex)
        {
            throw new BillingException(504, "The billing provider timed out.", ex);
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRaw(ex.Error, "The billing provider returned an error.");
        }
    }

    private BillingException MapJsonException(JsonException ex)
    {
        var status = MaxioLastHttp.Status.Value;
        if (status is { } code && (int)code >= 400 && (int)code < 500)
        {
            return new BillingException((int)code, "The billing provider rejected the request.", ex);
        }

        return new BillingException(502, "The billing provider returned a response that could not be processed.", ex);
    }

    private static BillingException MapRaw(RawError raw, string fallback)
    {
        var code = (int)raw.StatusCode;
        if (code < 400)
        {
            code = 502;
        }
        else if (code >= 500)
        {
            code = 502;
        }

        return new BillingException(code, fallback);
    }

    private static BillingException MapListProductsError(SdkException<ListProductsForProductFamilyError> ex)
    {
        if (ex.Error.TryGetString(out _))
        {
            return new BillingException(404, "The configured subscription catalog was not found.", ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return MapRaw(raw, "The subscription catalog could not be loaded.");
        }

        return new BillingException(502, "The subscription catalog could not be loaded.", ex);
    }

    private static BillingException MapCreateCustomerError(SdkException<CreateCustomerError> ex)
    {
        if (ex.Error.TryGetCustomerErrorResponse1(out _))
        {
            return new BillingException(422, "The billing provider rejected the customer details.", ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return MapRaw(raw, "The billing provider rejected the customer details.");
        }

        return new BillingException(422, "The billing provider rejected the customer details.", ex);
    }

    private static BillingException MapCreateSubscriptionError(SdkException<CreateSubscriptionError> ex)
    {
        if (ex.Error.TryGetErrorListResponse1(out var list)
            && list.Errors is { Count: > 0 }
            && !string.IsNullOrWhiteSpace(list.Errors[0]))
        {
            return new BillingException(422, list.Errors[0], ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return MapRaw(raw, "The subscription could not be created.");
        }

        return new BillingException(422, "The subscription could not be created.", ex);
    }

    private static BillingException MapFindSubscriptionError(SdkException<FindSubscriptionError> ex)
    {
        if (ex.Error.TryGetNoContent(out _))
        {
            return new BillingException(404, "The subscription was not found.", ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return MapRaw(raw, "The subscription could not be loaded.");
        }

        return new BillingException(502, "The subscription could not be loaded.", ex);
    }

    private static ShopperSubscription MapSubscription(Subscription? subscription, Product? product)
    {
        if (subscription is null)
        {
            throw new BillingException(502, "The billing provider returned a response that could not be processed.");
        }

        var handle = subscription.Product?.Handle ?? product?.Handle ?? string.Empty;
        var name = subscription.Product?.Name ?? product?.Name ?? handle;
        var priceCents = subscription.ProductPriceInCents ?? product?.PriceInCents ?? 0;

        return new ShopperSubscription(
            Id: RequireId(subscription.Id, "subscription"),
            ProductHandle: handle,
            ProductName: name,
            Price: ToMoney(priceCents),
            State: subscription.State?.Value ?? string.Empty,
            NextBillingAt: subscription.NextAssessmentAt,
            CurrentPeriodEndsAt: subscription.CurrentPeriodEndsAt);
    }

    private static SubscriptionPlan ToPlan(Product product)
    {
        return new SubscriptionPlan(
            Handle: product.Handle!,
            Name: product.Name!,
            Price: ToMoney(product.PriceInCents ?? 0),
            Interval: product.Interval ?? 1,
            IntervalUnit: product.IntervalUnit?.Value ?? "month",
            RequiresCreditCard: product.RequireCreditCard ?? false);
    }

    private static decimal ToMoney(long cents) => cents / 100m;

    private static int RequireId(int? id, string resource)
    {
        if (id is null)
        {
            throw new BillingException(502, "The billing provider returned a response that could not be processed.");
        }

        return id.Value;
    }

    private static bool IsOpen(string? state) =>
        !string.IsNullOrWhiteSpace(state) && !TerminalStates.Contains(state);

    private static string SubscriptionReference(string buyerId, string productHandle) =>
        $"{buyerId}:{productHandle}";

    private static (string First, string Last) SplitName(string? displayName, string email)
    {
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            var parts = displayName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2)
            {
                return (parts[0], parts[1]);
            }

            return (parts[0], "Customer");
        }

        var local = email.Split('@')[0];
        return (string.IsNullOrWhiteSpace(local) ? "Shopper" : local, "eShop");
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey)
            || string.IsNullOrWhiteSpace(_options.Subdomain)
            || string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw new BillingException(503, "Subscription billing is not configured.");
        }
    }
}
