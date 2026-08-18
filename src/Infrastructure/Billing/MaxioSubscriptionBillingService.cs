using System;
using System.Collections.Concurrent;
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
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private const int ProductPageSize = 20;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscribeGates = new();

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

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var family = await ResolveProductFamilyAsync(cancellationToken).ConfigureAwait(false);
        var plans = new List<SubscriptionPlan>();
        var page = 1;

        while (true)
        {
            var batch = await ListProductsPageAsync(family.Id!.Value.ToString(), page, cancellationToken).ConfigureAwait(false);
            foreach (var item in batch)
            {
                var product = item.Product;
                if (string.IsNullOrWhiteSpace(product.Handle))
                {
                    continue;
                }

                plans.Add(MapPlan(product));
            }

            if (batch.Count < ProductPageSize)
            {
                break;
            }

            page++;
        }

        return plans;
    }

    public async Task<SubscribeToPlanResult> SubscribeAsync(ShopperIdentity shopper, string productHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new BillingException(400, "A product handle is required.");
        }

        var gate = SubscribeGates.GetOrAdd(shopper.UserId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var customer = await EnsureCustomerAsync(shopper, cancellationToken).ConfigureAwait(false);
            var existing = await FindLiveSubscriptionAsync(customer.Id!.Value, productHandle, cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                return new SubscribeToPlanResult { Subscription = existing, Created = false };
            }

            await AssertProductAllowsSubscribeAsync(productHandle, cancellationToken).ConfigureAwait(false);

            try
            {
                var created = await CreateSubscriptionAsync(customer.Id.Value, shopper.UserId, productHandle, cancellationToken).ConfigureAwait(false);
                if (created is not null)
                {
                    return new SubscribeToPlanResult { Subscription = created, Created = true };
                }
            }
            catch (Exception ex) when (IsUnknownWriteOutcome(ex))
            {
                _logger.LogWarning(ex, "Subscribe write outcome unknown for user {UserId} plan {ProductHandle}; reconciling.", shopper.UserId, productHandle);
            }

            var reconciled = await FindLiveSubscriptionAsync(customer.Id.Value, productHandle, cancellationToken).ConfigureAwait(false);
            if (reconciled is not null)
            {
                return new SubscribeToPlanResult { Subscription = reconciled, Created = false };
            }

            throw new BillingException(502, "The subscription request may have been received but could not be confirmed. Check your subscriptions before retrying.");
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<ShopperSubscription>> ListMySubscriptionsAsync(string customerReference, CancellationToken cancellationToken)
    {
        var customer = await TryReadCustomerAsync(customerReference, cancellationToken).ConfigureAwait(false);
        if (customer is null)
        {
            return Array.Empty<ShopperSubscription>();
        }

        EnsureCustomerId(customer);
        var listed = await ListCustomerSubscriptionsAsync(customer.Id!.Value, cancellationToken).ConfigureAwait(false);
        var results = new List<ShopperSubscription>();
        foreach (var envelope in listed)
        {
            if (envelope.Subscription is null)
            {
                continue;
            }

            results.Add(MapSubscription(envelope.Subscription));
        }

        return results;
    }

    private async Task<ProductFamily> ResolveProductFamilyAsync(CancellationToken cancellationToken)
    {
        var handle = _options.ProductFamilyHandle;
        if (string.IsNullOrWhiteSpace(handle))
        {
            throw new BillingException(503, "Maxio billing is not configured (Maxio:ProductFamilyHandle is missing).");
        }

        IReadOnlyList<ProductFamilyResponse> families;
        try
        {
            families = await _client.ProductFamilies.ListProductFamilies(
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                ct: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw Translate(ex, "Unable to list subscription plans.");
        }

        foreach (var envelope in families)
        {
            var family = envelope.ProductFamily;
            if (family is null)
            {
                continue;
            }

            if (family.Handle == handle)
            {
                if (family.Id is null)
                {
                    throw new BillingException(502, "The billing provider returned a product family without an id.");
                }

                return family;
            }
        }

        throw new BillingException(404, $"No product family was found with handle '{handle}'.");
    }

    private async Task<IReadOnlyList<ProductResponse>> ListProductsPageAsync(string productFamilyId, int page, CancellationToken cancellationToken)
    {
        try
        {
            return await _client.ProductFamilies.ListProductsForProductFamily(
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
                ct: cancellationToken).ConfigureAwait(false);
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out _))
            {
                throw new BillingException(404, "The configured product family was not found.");
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw MapRaw(raw, "Unable to list subscription plans.");
            }

            throw new BillingException(502, "Unable to list subscription plans.", ex);
        }
        catch (Exception ex)
        {
            throw Translate(ex, "Unable to list subscription plans.");
        }
    }

    private async Task AssertProductAllowsSubscribeAsync(string productHandle, CancellationToken cancellationToken)
    {
        ProductResponse response;
        try
        {
            response = await _client.Products.ReadProductByHandle(productHandle, ct: cancellationToken).ConfigureAwait(false);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            throw new SubscriptionPlanNotFoundException(productHandle);
        }
        catch (Exception ex)
        {
            throw Translate(ex, "Unable to load the requested subscription plan.");
        }

        var product = response.Product;
        if (product.RequireCreditCard == true)
        {
            throw new BillingException(400, "This plan requires a payment method, which is not supported by this shop.");
        }
    }

    private async Task<Customer> EnsureCustomerAsync(ShopperIdentity shopper, CancellationToken cancellationToken)
    {
        var existing = await TryReadCustomerAsync(shopper.UserId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            EnsureCustomerId(existing);
            return existing;
        }

        try
        {
            CustomerResponse created;
            using (SingleSendWriteHandler.BeginWriteScope())
            {
                created = await _client.Customers.CreateCustomer(
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
                    ct: cancellationToken).ConfigureAwait(false);
            }

            EnsureCustomerId(created.Customer);
            return created.Customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out _) || ex.Error.TryGetRawError(out _))
            {
                var raced = await TryReadCustomerAsync(shopper.UserId, cancellationToken).ConfigureAwait(false);
                if (raced is not null)
                {
                    EnsureCustomerId(raced);
                    return raced;
                }

                throw new BillingException(422, "The billing customer could not be created.");
            }

            throw new BillingException(502, "The billing customer could not be created.", ex);
        }
        catch (Exception ex) when (IsUnknownWriteOutcome(ex))
        {
            var raced = await TryReadCustomerAsync(shopper.UserId, cancellationToken).ConfigureAwait(false);
            if (raced is not null)
            {
                EnsureCustomerId(raced);
                return raced;
            }

            throw new BillingException(502, "The customer request may have been received but could not be confirmed. Try again shortly.");
        }
        catch (Exception ex)
        {
            throw Translate(ex, "The billing customer could not be created.");
        }
    }

    private async Task<Customer?> TryReadCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference, ct: cancellationToken).ConfigureAwait(false);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex)
        {
            throw Translate(ex, "Unable to look up the billing customer.");
        }
    }

    private async Task<IReadOnlyList<SubscriptionResponse>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        try
        {
            return await _client.Customers.ListCustomerSubscriptions(customerId, ct: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw Translate(ex, "Unable to list subscriptions.");
        }
    }

    private async Task<ShopperSubscription?> FindLiveSubscriptionAsync(int customerId, string productHandle, CancellationToken cancellationToken)
    {
        var listed = await ListCustomerSubscriptionsAsync(customerId, cancellationToken).ConfigureAwait(false);
        foreach (var envelope in listed)
        {
            var subscription = envelope.Subscription;
            if (subscription is null)
            {
                continue;
            }

            if (subscription.Product?.Handle != productHandle)
            {
                continue;
            }

            if (IsLive(subscription.State))
            {
                return MapSubscription(subscription);
            }
        }

        return null;
    }

    private async Task<ShopperSubscription?> CreateSubscriptionAsync(int customerId, string userId, string productHandle, CancellationToken cancellationToken)
    {
        try
        {
            SubscriptionResponse response;
            using (SingleSendWriteHandler.BeginWriteScope())
            {
                response = await _client.Subscriptions.CreateSubscription(
                    body: new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            ProductHandle = productHandle,
                            CustomerId = customerId,
                            PaymentCollectionMethod = CollectionMethod.Remittance,
                            Reference = $"{userId}:{productHandle}"
                        }
                    },
                    ct: cancellationToken).ConfigureAwait(false);
            }

            return response.Subscription is null ? null : MapSubscription(response.Subscription);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var list))
            {
                var detail = list.Errors.Count == 0 ? "The subscription could not be created." : string.Join(" ", list.Errors);
                throw new BillingException(422, detail);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw MapRaw(raw, "The subscription could not be created.");
            }

            throw new BillingException(502, "The subscription could not be created.", ex);
        }
        catch (Exception ex) when (IsUnknownWriteOutcome(ex))
        {
            throw;
        }
        catch (Exception ex)
        {
            throw Translate(ex, "The subscription could not be created.");
        }
    }

    private static void EnsureCustomerId(Customer customer)
    {
        if (customer.Id is null)
        {
            throw new BillingException(502, "The billing provider returned a customer without an id.");
        }
    }

    private static bool IsLive(SubscriptionState? state)
    {
        if (state is null)
        {
            return false;
        }

        return state == SubscriptionState.Active
            || state == SubscriptionState.Trialing
            || state == SubscriptionState.Assessing
            || state == SubscriptionState.PastDue
            || state == SubscriptionState.SoftFailure
            || state == SubscriptionState.Unpaid
            || state == SubscriptionState.Pending
            || state == SubscriptionState.AwaitingSignup
            || state == SubscriptionState.OnHold
            || state == SubscriptionState.Paused
            || state == SubscriptionState.Suspended;
    }

    private static SubscriptionPlan MapPlan(Product product) => new()
    {
        Handle = product.Handle!,
        Name = product.Name ?? product.Handle!,
        PriceInCents = product.PriceInCents ?? 0,
        Interval = product.Interval ?? 1,
        IntervalUnit = product.IntervalUnit?.Value
    };

    private static ShopperSubscription MapSubscription(Subscription subscription) => new()
    {
        Id = subscription.Id,
        ProductHandle = subscription.Product?.Handle,
        ProductName = subscription.Product?.Name,
        PriceInCents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents,
        State = subscription.State?.Value,
        NextBillingDate = subscription.NextAssessmentAt,
        Interval = subscription.Product?.Interval,
        IntervalUnit = subscription.Product?.IntervalUnit?.Value
    };

    private static bool IsUnknownWriteOutcome(Exception ex) =>
        ex is DuplicateWritePreventedException
        || ex is HttpRequestException
        || ex is TaskCanceledException;

    private static BillingException Translate(Exception ex, string fallback)
    {
        if (ex is BillingException billing)
        {
            return billing;
        }

        if (ex is JsonException)
        {
            var status = LastStatusCaptureHandler.Last;
            if (status is { } code && (int)code >= 400)
            {
                var mapped = (int)code is >= 400 and < 500 ? (int)code : 502;
                return new BillingException(mapped == 401 || mapped == 403 ? 502 : mapped,
                    mapped is >= 400 and < 500 && mapped is not 401 and not 403
                        ? "The billing provider rejected the request."
                        : fallback,
                    ex);
            }

            return new BillingException(502, "The billing provider returned a response that could not be processed.", ex);
        }

        if (ex is SdkException<RawError> raw)
        {
            return MapRaw(raw.Error, fallback);
        }

        if (ex is HttpRequestException or TaskCanceledException)
        {
            return new BillingException(503, "The billing provider is temporarily unavailable.", ex);
        }

        return new BillingException(502, fallback, ex);
    }

    private static BillingException MapRaw(RawError raw, string fallback)
    {
        var status = (int)raw.StatusCode;
        return status switch
        {
            401 or 403 => new BillingException(502, "Billing provider authentication failed."),
            404 => new BillingException(404, fallback),
            422 => new BillingException(422, fallback),
            429 => new BillingException(503, "The billing provider is rate limiting requests. Try again shortly."),
            >= 400 and < 500 => new BillingException(status, fallback),
            _ => new BillingException(502, fallback)
        };
    }
}
