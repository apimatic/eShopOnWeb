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

public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly SubscriptionState[] LiveStates =
    {
        SubscriptionState.Pending,
        SubscriptionState.Trialing,
        SubscriptionState.Assessing,
        SubscriptionState.Active,
        SubscriptionState.SoftFailure,
        SubscriptionState.PastDue,
        SubscriptionState.Unpaid,
        SubscriptionState.AwaitingSignup
    };

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;
    private readonly TimeSpan _callBudget;

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioOptions> options,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
        var seconds = _options.CallTimeoutSeconds > 0 ? _options.CallTimeoutSeconds : 30;
        _callBudget = TimeSpan.FromSeconds(seconds);
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var familyHandle = _options.ProductFamilyHandle;
        if (string.IsNullOrWhiteSpace(familyHandle))
        {
            throw new BillingException(503, "Billing is not configured.");
        }

        try
        {
            var plans = new List<SubscriptionPlan>();
            const int pageSize = 100;
            var page = 1;

            while (true)
            {
                var rows = await Bounded(
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
                        perPage: pageSize,
                        ct: ct),
                    cancellationToken);

                if (rows is null || rows.Count == 0)
                {
                    break;
                }

                foreach (var row in rows)
                {
                    var product = row.Product;
                    if (product is null || product.ArchivedAt is not null)
                    {
                        continue;
                    }

                    var productFamilyHandle = product.ProductFamily?.Handle;
                    if (!string.Equals(productFamilyHandle, familyHandle, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    plans.Add(MapPlan(product));
                }

                if (rows.Count < pageSize)
                {
                    break;
                }

                page++;
            }

            return plans;
        }
        catch (BillingException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw Translate(ex, "Unable to load subscription plans.");
        }
    }

    public async Task<ShopperSubscription> SubscribeAsync(
        ShopperIdentity shopper,
        string productHandle,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new BillingException(400, "A product handle is required.");
        }

        productHandle = productHandle.Trim();
        var subscriptionReference = $"{shopper.UserId}:{productHandle}";

        try
        {
            var existing = await FindLiveSubscriptionByReference(subscriptionReference, cancellationToken);
            if (existing is not null)
            {
                return MapSubscription(existing);
            }

            var customer = await EnsureCustomerAsync(shopper, cancellationToken);

            var byCustomer = await ListLiveSubscriptionsForCustomer(customer.Id, productHandle, cancellationToken);
            if (byCustomer is not null)
            {
                return MapSubscription(byCustomer);
            }

            return await CreateSubscriptionOnce(customer, productHandle, subscriptionReference, cancellationToken);
        }
        catch (BillingException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw Translate(ex, "Unable to create the subscription.");
        }
    }

    public async Task<IReadOnlyList<ShopperSubscription>> ListMySubscriptionsAsync(
        ShopperIdentity shopper,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();

        try
        {
            var customer = await TryReadCustomerAsync(shopper.UserId, cancellationToken);
            if (customer?.Id is null)
            {
                return Array.Empty<ShopperSubscription>();
            }

            var rows = await Bounded(
                ct => _client.Customers.ListCustomerSubscriptions(customer.Id.Value, ct: ct),
                cancellationToken);

            if (rows is null || rows.Count == 0)
            {
                return Array.Empty<ShopperSubscription>();
            }

            return rows
                .Select(row => row.Subscription)
                .Where(sub => sub is not null)
                .Select(sub => MapSubscription(sub!))
                .ToList();
        }
        catch (BillingException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw Translate(ex, "Unable to load subscriptions.");
        }
    }

    private async Task<Customer> EnsureCustomerAsync(ShopperIdentity shopper, CancellationToken cancellationToken)
    {
        var existing = await TryReadCustomerAsync(shopper.UserId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = shopper.FirstName,
                LastName = shopper.LastName,
                Email = shopper.Email,
                Reference = shopper.UserId
            }
        };

        using (SingleFlightWriteHandler.BeginWrite())
        {
            try
            {
                var created = await Bounded(
                    ct => _client.Customers.CreateCustomer(body, ct: ct),
                    cancellationToken);

                return created.Customer ?? throw new BillingException(502, "The billing provider returned a response that could not be processed.");
            }
            catch (DuplicateWritePreventedException)
            {
                _logger.LogWarning("Customer create was blocked after a possible send; reconciling by reference.");
            }
            catch (SdkException<CreateCustomerError> ex)
            {
                var mapped = MapCreateCustomerError(ex);
                var retried = await TryReadCustomerAsync(shopper.UserId, cancellationToken);
                if (retried is not null)
                {
                    return retried;
                }

                throw mapped;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                var retried = await TryReadCustomerAsync(shopper.UserId, cancellationToken);
                if (retried is not null)
                {
                    return retried;
                }

                throw Translate(ex, "Unable to create the billing customer.");
            }
        }

        var reconciled = await TryReadCustomerAsync(shopper.UserId, cancellationToken);
        if (reconciled is not null)
        {
            return reconciled;
        }

        throw new BillingException(502, "The billing request may have been processed; refresh and try again.");
    }

    private async Task<ShopperSubscription> CreateSubscriptionOnce(
        Customer customer,
        string productHandle,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        if (customer.Id is null)
        {
            throw new BillingException(502, "The billing provider returned a response that could not be processed.");
        }

        var body = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = productHandle,
                CustomerId = customer.Id,
                Reference = subscriptionReference,
                PaymentCollectionMethod = CollectionMethod.Invoice
            }
        };

        using (SingleFlightWriteHandler.BeginWrite())
        {
            try
            {
                var created = await Bounded(
                    ct => _client.Subscriptions.CreateSubscription(body, ct: ct),
                    cancellationToken);

                var subscription = created.Subscription
                    ?? throw new BillingException(502, "The billing provider returned a response that could not be processed.");
                return MapSubscription(subscription);
            }
            catch (DuplicateWritePreventedException)
            {
                _logger.LogWarning("Subscription create was blocked after a possible send; reconciling by reference.");
            }
            catch (SdkException<CreateSubscriptionError> ex)
            {
                var mapped = MapCreateSubscriptionError(ex);
                var existing = await FindLiveSubscriptionByReference(subscriptionReference, cancellationToken)
                    ?? await ListLiveSubscriptionsForCustomer(customer.Id, productHandle, cancellationToken);
                if (existing is not null)
                {
                    return MapSubscription(existing);
                }

                throw mapped;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                var existing = await FindLiveSubscriptionByReference(subscriptionReference, cancellationToken)
                    ?? await ListLiveSubscriptionsForCustomer(customer.Id, productHandle, cancellationToken);
                if (existing is not null)
                {
                    return MapSubscription(existing);
                }

                throw Translate(ex, "Unable to create the subscription.");
            }
        }

        var reconciled = await FindLiveSubscriptionByReference(subscriptionReference, cancellationToken)
            ?? await ListLiveSubscriptionsForCustomer(customer.Id, productHandle, cancellationToken);
        if (reconciled is not null)
        {
            return MapSubscription(reconciled);
        }

        throw new BillingException(502, "The billing request may have been processed; refresh and try again.");
    }

    private async Task<Customer?> TryReadCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Bounded(
                ct => _client.Customers.ReadCustomerByReference(reference, ct: ct),
                cancellationToken);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (IsNotFound(ex.Error.StatusCode))
        {
            return null;
        }
    }

    private async Task<Subscription?> FindLiveSubscriptionByReference(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Bounded(
                ct => _client.Subscriptions.FindSubscription(reference, ct: ct),
                cancellationToken);
            var subscription = response.Subscription;
            return IsLive(subscription) ? subscription : null;
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out RawError noContent) && IsNotFound(noContent.StatusCode))
            {
                return null;
            }

            if (ex.Error.TryGetRawError(out RawError raw) && IsNotFound(raw.StatusCode))
            {
                return null;
            }

            throw MapFindSubscriptionError(ex);
        }
    }

    private async Task<Subscription?> ListLiveSubscriptionsForCustomer(
        int? customerId,
        string productHandle,
        CancellationToken cancellationToken)
    {
        if (customerId is null)
        {
            return null;
        }

        var rows = await Bounded(
            ct => _client.Customers.ListCustomerSubscriptions(customerId.Value, ct: ct),
            cancellationToken);

        return rows?
            .Select(row => row.Subscription)
            .FirstOrDefault(sub =>
                IsLive(sub)
                && string.Equals(sub!.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_callBudget);
        return await call(cts.Token);
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey) || string.IsNullOrWhiteSpace(_options.Subdomain))
        {
            throw new BillingException(503, "Billing is not configured.");
        }
    }

    private static bool IsLive(Subscription? subscription)
    {
        if (subscription?.State is null)
        {
            return false;
        }

        return LiveStates.Contains(subscription.State);
    }

    private static bool IsNotFound(HttpStatusCode statusCode) => statusCode == HttpStatusCode.NotFound;

    private static SubscriptionPlan MapPlan(Product product)
    {
        return new SubscriptionPlan(
            product.Id ?? 0,
            product.Handle ?? string.Empty,
            product.Name ?? product.Handle ?? string.Empty,
            product.Description,
            CentsToAmount(product.PriceInCents),
            FormatInterval(product.Interval, product.IntervalUnit));
    }

    private static ShopperSubscription MapSubscription(Subscription subscription)
    {
        var product = subscription.Product;
        var handle = product?.Handle ?? string.Empty;
        var name = product?.Name ?? handle;
        var priceCents = subscription.ProductPriceInCents ?? product?.PriceInCents;
        var state = subscription.State?.Value ?? "unknown";
        var nextBilling = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt;

        return new ShopperSubscription(
            subscription.Id ?? 0,
            handle,
            name,
            CentsToAmount(priceCents),
            state,
            nextBilling,
            subscription.CurrentPeriodEndsAt,
            product is null ? null : FormatInterval(product.Interval, product.IntervalUnit));
    }

    private static decimal CentsToAmount(long? cents) => (cents ?? 0) / 100m;

    private static string FormatInterval(int? interval, IntervalUnit? unit)
    {
        var count = interval ?? 1;
        var unitValue = unit?.Value ?? IntervalUnit.Month.Value;
        if (count == 1)
        {
            return $"per {unitValue}";
        }

        return $"every {count} {unitValue}s";
    }

    private BillingException Translate(Exception exception, string fallback)
    {
        switch (exception)
        {
            case BillingException billing:
                return billing;
            case SdkException<CreateCustomerError> createCustomer:
                return MapCreateCustomerError(createCustomer);
            case SdkException<CreateSubscriptionError> createSubscription:
                return MapCreateSubscriptionError(createSubscription);
            case SdkException<FindSubscriptionError> findSubscription:
                return MapFindSubscriptionError(findSubscription);
            case SdkException<RawError> raw:
                return MapRaw(raw.Error, fallback);
            case JsonException:
                return MapJsonException(fallback);
            case DuplicateWritePreventedException:
                return new BillingException(502, "The billing request may have been processed; refresh and try again.", exception);
            case TaskCanceledException:
                return new BillingException(504, "The billing provider timed out.", exception);
            case HttpRequestException:
                return new BillingException(503, "The billing provider is unreachable.", exception);
            default:
                _logger.LogWarning(exception, "Unexpected billing failure.");
                return new BillingException(502, fallback, exception);
        }
    }

    private static BillingException MapCreateCustomerError(SdkException<CreateCustomerError> exception)
    {
        if (exception.Error.TryGetCustomerErrorResponse1(out _))
        {
            return new BillingException(422, "The billing customer could not be created.", exception);
        }

        if (exception.Error.TryGetRawError(out RawError raw))
        {
            return MapRaw(raw, "The billing customer could not be created.");
        }

        return new BillingException(502, "The billing customer could not be created.", exception);
    }

    private static BillingException MapCreateSubscriptionError(SdkException<CreateSubscriptionError> exception)
    {
        if (exception.Error.TryGetErrorListResponse1(out ErrorListResponse1 list))
        {
            return new BillingException(422, FirstError(list) ?? "The subscription could not be created.", exception);
        }

        if (exception.Error.TryGetRawError(out RawError raw))
        {
            return MapRaw(raw, "The subscription could not be created.");
        }

        return new BillingException(422, "The subscription could not be created.", exception);
    }

    private static BillingException MapFindSubscriptionError(SdkException<FindSubscriptionError> exception)
    {
        if (exception.Error.TryGetNoContent(out RawError noContent))
        {
            return MapRaw(noContent, "The subscription was not found.");
        }

        if (exception.Error.TryGetRawError(out RawError raw))
        {
            return MapRaw(raw, "Unable to look up the subscription.");
        }

        return new BillingException(502, "Unable to look up the subscription.", exception);
    }

    private static BillingException MapRaw(RawError raw, string fallback)
    {
        var status = (int)raw.StatusCode;
        if (status is >= 400 and < 500)
        {
            return new BillingException(status == 404 ? 404 : 422, fallback);
        }

        if (status == 0)
        {
            return new BillingException(502, fallback);
        }

        return new BillingException(status >= 500 ? 502 : 422, fallback);
    }

    private static BillingException MapJsonException(string fallback)
    {
        var status = LastStatusHandler.LastStatus;
        if (status is >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError)
        {
            return new BillingException((int)status.Value, fallback);
        }

        return new BillingException(502, "The billing provider returned a response that could not be processed.");
    }

    private static string? FirstError(ErrorListResponse1 list)
    {
        var message = list.Errors?.FirstOrDefault(e => !string.IsNullOrWhiteSpace(e));
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        // Keep the provider phrase but never dump a stack or type name.
        return message.Length > 300 ? "The subscription could not be created." : message;
    }
}
