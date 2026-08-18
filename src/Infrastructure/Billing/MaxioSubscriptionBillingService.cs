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
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly IAppLogger<MaxioSubscriptionBillingService> _logger;
    private readonly ConcurrentUserGate _subscribeGate = new();

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioOptions> options,
        IAppLogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var familyHandle = _options.ProductFamilyHandle.Trim();
        var products = await ListFamilyProductsAsync(familyHandle, cancellationToken);
        return products.Select(MapPlan).ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(
        ShopperProfile shopper,
        string productHandle,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        ValidateShopper(shopper);

        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new MaxioBillingException(StatusCodes.BadRequest, "productHandle is required.");
        }

        productHandle = productHandle.Trim();
        await EnsurePlanInFamilyAsync(productHandle, cancellationToken);

        var gateKey = $"{shopper.BillingReference}:{productHandle}";
        using (await _subscribeGate.EnterAsync(gateKey, cancellationToken))
        {
            var customerId = await EnsureCustomerAsync(shopper, cancellationToken);
            var subscriptionReference = BuildSubscriptionReference(shopper, productHandle);

            var existing = await FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation("Returning existing Maxio subscription {SubscriptionId} for shopper {UserId}", existing.Id, shopper.UserId);
                return new SubscribeResult(existing, Created: false);
            }

            try
            {
                var created = await CreateSubscriptionWithoutCardAsync(
                    customerId, productHandle, subscriptionReference, cancellationToken);

                var enrolled = MapSubscriptionOrNull(created.Subscription)
                    ?? await FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
                if (enrolled is null)
                {
                    throw new MaxioBillingException(
                        StatusCodes.BadGateway,
                        "The billing provider accepted the subscription but returned an unreadable confirmation.");
                }

                _logger.LogInformation("Created Maxio subscription {SubscriptionId} for shopper {UserId}", enrolled.Id, shopper.UserId);
                return new SubscribeResult(enrolled, Created: true);
            }
            catch (MaxioBillingException)
            {
                throw;
            }
            catch (SdkException<CreateSubscriptionError> ex)
            {
                var recovered = await FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
                if (recovered is not null)
                {
                    return new SubscribeResult(recovered, Created: false);
                }

                throw MapCreateSubscriptionError(ex);
            }
            catch (Exception ex) when (IsUnknownWriteOutcome(ex))
            {
                var recovered = await FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken)
                    ?? await FindCustomerSubscriptionByReferenceAsync(customerId, subscriptionReference, productHandle, cancellationToken);
                if (recovered is not null)
                {
                    return new SubscribeResult(recovered, Created: false);
                }

                throw MapUnknownWriteOutcome("subscribe", ex);
            }
        }
    }

    public async Task<IReadOnlyList<ShopperSubscription>> ListMySubscriptionsAsync(
        ShopperProfile shopper,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        ValidateShopper(shopper);

        var customer = await ReadCustomerByReferenceOrNullAsync(shopper.BillingReference, cancellationToken);
        if (customer?.Id is null)
        {
            return Array.Empty<ShopperSubscription>();
        }

        try
        {
            var rows = await _client.Customers.ListCustomerSubscriptions(customer.Id.Value, ct: cancellationToken);
            return (rows ?? Array.Empty<SubscriptionResponse>())
                .Select(r => MapSubscriptionOrNull(r.Subscription))
                .Where(s => s is not null)
                .Select(s => s!)
                .ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRawError("list subscriptions", ex.Error);
        }
        catch (Exception ex) when (IsTransportOrParse(ex))
        {
            throw MapTransportOrParse("list subscriptions", ex);
        }
    }

    private async Task<IReadOnlyList<Product>> ListFamilyProductsAsync(string familyHandle, CancellationToken cancellationToken)
    {
        var results = new List<Product>();
        var page = 1;
        const int perPage = 200;

        try
        {
            while (true)
            {
                var batch = await _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: "handle:" + familyHandle,
                    dateField: null,
                    filter: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    includeArchived: false,
                    include: null,
                    page: page,
                    perPage: perPage,
                    ct: cancellationToken);

                if (batch is null || batch.Count == 0)
                {
                    break;
                }

                foreach (var envelope in batch)
                {
                    results.Add(envelope.Product);
                }

                if (batch.Count < perPage)
                {
                    break;
                }

                page++;
            }
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            throw MapListProductsError(ex);
        }
        catch (Exception ex) when (IsTransportOrParse(ex))
        {
            throw MapTransportOrParse("list plans", ex);
        }

        return results;
    }

    private async Task<SubscriptionResponse> CreateSubscriptionWithoutCardAsync(
        int customerId,
        string productHandle,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        // RI sites: remittance. Legacy statements: invoice. Automatic requires a card.
        var methods = new[] { CollectionMethod.Remittance, CollectionMethod.Invoice };
        SdkException<CreateSubscriptionError>? lastCreateError = null;

        foreach (var method in methods)
        {
            try
            {
                using (MaxioWriteOnceScope.Begin())
                {
                    return await _client.Subscriptions.CreateSubscription(
                        body: new CreateSubscriptionRequest
                        {
                            Subscription = new CreateSubscription
                            {
                                ProductHandle = productHandle,
                                CustomerId = customerId,
                                Reference = subscriptionReference,
                                PaymentCollectionMethod = method
                            }
                        },
                        ct: cancellationToken);
                }
            }
            catch (SdkException<CreateSubscriptionError> ex)
            {
                lastCreateError = ex;
                var recovered = await FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
                if (recovered is not null)
                {
                    return new SubscriptionResponse { Subscription = null };
                }

                _logger.LogWarning(
                    "CreateSubscription with {CollectionMethod} was rejected; trying next collection method if any.",
                    method.Value);
            }
        }

        if (lastCreateError is not null)
        {
            throw lastCreateError;
        }

        throw new MaxioBillingException(StatusCodes.BadGateway, "Unable to create the subscription.");
    }

    private async Task EnsurePlanInFamilyAsync(string productHandle, CancellationToken cancellationToken)
    {
        Product? product = null;
        try
        {
            var response = await _client.Products.ReadProductByHandle(productHandle, ct: cancellationToken);
            product = response.Product;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            throw new MaxioBillingException(StatusCodes.NotFound, $"Unknown subscription plan '{productHandle}'.");
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRawError("read plan", ex.Error);
        }
        catch (Exception ex) when (IsTransportOrParse(ex))
        {
            throw MapTransportOrParse("read plan", ex);
        }

        var familyHandle = product.ProductFamily?.Handle;
        if (!string.IsNullOrWhiteSpace(familyHandle))
        {
            if (!string.Equals(familyHandle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
            {
                throw new MaxioBillingException(StatusCodes.NotFound, $"Unknown subscription plan '{productHandle}'.");
            }

            return;
        }

        var familyProducts = await ListFamilyProductsAsync(_options.ProductFamilyHandle.Trim(), cancellationToken);
        if (!familyProducts.Any(p => string.Equals(p.Handle, productHandle, StringComparison.OrdinalIgnoreCase)))
        {
            throw new MaxioBillingException(StatusCodes.NotFound, $"Unknown subscription plan '{productHandle}'.");
        }
    }

    private async Task<int> EnsureCustomerAsync(ShopperProfile shopper, CancellationToken cancellationToken)
    {
        var existing = await ReadCustomerByReferenceOrNullAsync(shopper.BillingReference, cancellationToken);
        if (existing?.Id is int existingId)
        {
            return existingId;
        }

        var (firstName, lastName) = SplitName(shopper);
        try
        {
            CustomerResponse created;
            using (MaxioWriteOnceScope.Begin())
            {
                created = await _client.Customers.CreateCustomer(
                    body: new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer
                        {
                            FirstName = firstName,
                            LastName = lastName,
                            Email = shopper.Email,
                            Reference = shopper.BillingReference
                        }
                    },
                    ct: cancellationToken);
            }

            if (created.Customer.Id is int createdId)
            {
                return createdId;
            }

            var reread = await ReadCustomerByReferenceOrNullAsync(shopper.BillingReference, cancellationToken);
            if (reread?.Id is int rereadId)
            {
                return rereadId;
            }

            throw new MaxioBillingException(
                StatusCodes.BadGateway,
                "The billing provider accepted the customer but returned an unreadable confirmation.");
        }
        catch (MaxioBillingException)
        {
            throw;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            var raced = await ReadCustomerByReferenceOrNullAsync(shopper.BillingReference, cancellationToken);
            if (raced?.Id is int racedId)
            {
                return racedId;
            }

            throw MapCreateCustomerError(ex);
        }
        catch (Exception ex) when (IsUnknownWriteOutcome(ex))
        {
            var recovered = await ReadCustomerByReferenceOrNullAsync(shopper.BillingReference, cancellationToken);
            if (recovered?.Id is int recoveredId)
            {
                return recoveredId;
            }

            throw MapUnknownWriteOutcome("ensure customer", ex);
        }
    }

    private async Task<Customer?> ReadCustomerByReferenceOrNullAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference, ct: cancellationToken);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRawError("lookup customer", ex.Error);
        }
        catch (Exception ex) when (IsTransportOrParse(ex))
        {
            throw MapTransportOrParse("lookup customer", ex);
        }
    }

    private async Task<ShopperSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.Subscriptions.FindSubscription(reference: reference, ct: cancellationToken);
            return MapSubscriptionOrNull(response.Subscription);
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out RawError _))
            {
                return null;
            }

            if (ex.Error.TryGetRawError(out RawError raw) && raw.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            throw MapFindSubscriptionError(ex);
        }
        catch (Exception ex) when (IsTransportOrParse(ex))
        {
            throw MapTransportOrParse("find subscription", ex);
        }
    }

    private async Task<ShopperSubscription?> FindCustomerSubscriptionByReferenceAsync(
        int customerId,
        string reference,
        string productHandle,
        CancellationToken cancellationToken)
    {
        try
        {
            var rows = await _client.Customers.ListCustomerSubscriptions(customerId, ct: cancellationToken);
            var mapped = (rows ?? Array.Empty<SubscriptionResponse>())
                .Select(r => MapSubscriptionOrNull(r.Subscription))
                .Where(s => s is not null)
                .Select(s => s!)
                .ToList();

            return mapped.FirstOrDefault(s =>
                       string.Equals(s.Reference, reference, StringComparison.Ordinal))
                   ?? mapped.FirstOrDefault(s =>
                       string.Equals(s.ProductHandle, productHandle, StringComparison.OrdinalIgnoreCase));
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRawError("list subscriptions", ex.Error);
        }
        catch (Exception ex) when (IsTransportOrParse(ex))
        {
            throw MapTransportOrParse("list subscriptions", ex);
        }
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey) ||
            (string.IsNullOrWhiteSpace(_options.Subdomain) && string.IsNullOrWhiteSpace(_options.BaseUrl)) ||
            string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw new MaxioBillingException(
                StatusCodes.ServiceUnavailable,
                "Subscription billing is not configured.");
        }
    }

    private static void ValidateShopper(ShopperProfile shopper)
    {
        if (string.IsNullOrWhiteSpace(shopper.UserId))
        {
            throw new MaxioBillingException(StatusCodes.BadRequest, "A signed-in shopper is required.");
        }

        if (string.IsNullOrWhiteSpace(shopper.Email))
        {
            throw new MaxioBillingException(StatusCodes.BadRequest, "The signed-in shopper has no email address.");
        }
    }

    private static string BuildSubscriptionReference(ShopperProfile shopper, string productHandle) =>
        $"{shopper.BillingReference}:{productHandle}";

    private static (string FirstName, string LastName) SplitName(ShopperProfile shopper)
    {
        var source = !string.IsNullOrWhiteSpace(shopper.UserName) ? shopper.UserName! : shopper.Email;
        var at = source.IndexOf('@');
        var local = at > 0 ? source[..at] : source;
        if (string.IsNullOrWhiteSpace(local))
        {
            local = "Shopper";
        }

        return (local, "Customer");
    }

    private static SubscriptionPlan MapPlan(Product product)
    {
        var cents = product.PriceInCents ?? 0L;
        return new SubscriptionPlan(
            Handle: product.Handle ?? string.Empty,
            Name: product.Name ?? product.Handle ?? "Plan",
            Description: product.Description,
            PriceInCents: cents,
            Price: cents / 100m,
            Interval: product.Interval ?? 1,
            IntervalUnit: product.IntervalUnit?.Value,
            ProductFamilyHandle: product.ProductFamily?.Handle,
            RequireCreditCard: product.RequireCreditCard ?? false);
    }

    private static ShopperSubscription? MapSubscriptionOrNull(Subscription? subscription)
    {
        if (subscription is null || subscription.Id is null)
        {
            return null;
        }

        var cents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? 0L;
        var nextBilling = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt;
        return new ShopperSubscription(
            Id: subscription.Id.Value,
            State: subscription.State?.Value ?? string.Empty,
            ProductHandle: subscription.Product?.Handle,
            ProductName: subscription.Product?.Name,
            PriceInCents: cents,
            Price: cents / 100m,
            NextBillingDate: nextBilling,
            CurrentPeriodEndsAt: subscription.CurrentPeriodEndsAt,
            Reference: subscription.Reference);
    }

    private static MaxioBillingException MapListProductsError(SdkException<ListProductsForProductFamilyError> ex)
    {
        if (ex.Error.TryGetString(out var message))
        {
            return new MaxioBillingException(StatusCodes.NotFound, CallerSafe(message, "Subscription plans were not found."), ex);
        }

        if (ex.Error.TryGetRawError(out RawError raw))
        {
            return MapRawError("list plans", raw, ex);
        }

        return new MaxioBillingException(StatusCodes.BadGateway, "The billing provider rejected the request to list plans.", ex);
    }

    private static MaxioBillingException MapCreateCustomerError(SdkException<CreateCustomerError> ex)
    {
        if (ex.Error.TryGetCustomerErrorResponse1(out var _))
        {
            return new MaxioBillingException(StatusCodes.UnprocessableEntity, "Unable to create the billing customer.", ex);
        }

        if (ex.Error.TryGetRawError(out RawError raw))
        {
            return MapRawError("create customer", raw, ex);
        }

        return new MaxioBillingException(StatusCodes.BadGateway, "The billing provider rejected the customer.", ex);
    }

    private static MaxioBillingException MapCreateSubscriptionError(SdkException<CreateSubscriptionError> ex)
    {
        if (ex.Error.TryGetErrorListResponse1(out var body) && body.Errors is { Count: > 0 })
        {
            return new MaxioBillingException(StatusCodes.UnprocessableEntity, string.Join(" ", body.Errors), ex);
        }

        if (ex.Error.TryGetRawError(out RawError raw))
        {
            return MapRawError("create subscription", raw, ex);
        }

        return new MaxioBillingException(StatusCodes.UnprocessableEntity, "Unable to create the subscription.", ex);
    }

    private static MaxioBillingException MapFindSubscriptionError(SdkException<FindSubscriptionError> ex)
    {
        if (ex.Error.TryGetNoContent(out RawError noContent))
        {
            return MapRawError("find subscription", noContent, ex);
        }

        if (ex.Error.TryGetRawError(out RawError raw))
        {
            return MapRawError("find subscription", raw, ex);
        }

        return new MaxioBillingException(StatusCodes.BadGateway, "The billing provider rejected the subscription lookup.", ex);
    }

    private static MaxioBillingException MapRawError(string operation, RawError raw, Exception? inner = null)
    {
        var status = (int)raw.StatusCode;
        if (status == StatusCodes.NotFound)
        {
            return new MaxioBillingException(StatusCodes.NotFound, "The requested billing record was not found.", inner ?? new InvalidOperationException(operation));
        }

        if (status == StatusCodes.Unauthorized || status == StatusCodes.Forbidden)
        {
            return new MaxioBillingException(StatusCodes.BadGateway, "The billing provider rejected the request.", inner);
        }

        if (status >= 400 && status < 500)
        {
            return new MaxioBillingException(status, $"The billing provider rejected the request to {operation}.", inner);
        }

        return new MaxioBillingException(StatusCodes.BadGateway, $"The billing provider failed while handling {operation}.", inner);
    }

    private MaxioBillingException MapUnknownWriteOutcome(string operation, Exception ex)
    {
        _logger.LogWarning("Unknown outcome for Maxio {Operation}: {ExceptionType}", operation, ex.GetType().Name);
        if (ex is JsonException)
        {
            return new MaxioBillingException(
                StatusCodes.UnprocessableEntity,
                $"The billing provider rejected the request to {operation}.",
                ex);
        }

        return new MaxioBillingException(
            StatusCodes.ServiceUnavailable,
            $"The billing provider did not confirm {operation}. Retry after checking your subscriptions.",
            ex);
    }

    private static MaxioBillingException MapTransportOrParse(string operation, Exception ex)
    {
        if (ex is JsonException)
        {
            return new MaxioBillingException(
                StatusCodes.BadGateway,
                "The billing provider returned a response that could not be processed.",
                ex);
        }

        if (ex is TaskCanceledException)
        {
            return new MaxioBillingException(StatusCodes.GatewayTimeout, $"The billing provider timed out during {operation}.", ex);
        }

        return new MaxioBillingException(StatusCodes.BadGateway, $"The billing provider is unreachable during {operation}.", ex);
    }

    private static bool IsUnknownWriteOutcome(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or JsonException or DuplicateProviderWriteException;

    private static bool IsTransportOrParse(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or JsonException;

    private static string CallerSafe(string? providerMessage, string fallback) =>
        string.IsNullOrWhiteSpace(providerMessage) ? fallback : providerMessage.Trim();

    private static class StatusCodes
    {
        public const int BadRequest = 400;
        public const int Unauthorized = 401;
        public const int Forbidden = 403;
        public const int NotFound = 404;
        public const int UnprocessableEntity = 422;
        public const int BadGateway = 502;
        public const int ServiceUnavailable = 503;
        public const int GatewayTimeout = 504;
    }

    private sealed class ConcurrentUserGate
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _gates = new();

        public async Task<IDisposable> EnterAsync(string key, CancellationToken cancellationToken)
        {
            var gate = _gates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cancellationToken);
            return new Releaser(gate);
        }

        private sealed class Releaser : IDisposable
        {
            private readonly SemaphoreSlim _gate;
            public Releaser(SemaphoreSlim gate) => _gate = gate;
            public void Dispose() => _gate.Release();
        }
    }
}
