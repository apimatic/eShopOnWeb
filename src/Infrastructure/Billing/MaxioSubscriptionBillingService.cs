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
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ShopperLocks = new(StringComparer.Ordinal);

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
        EnsureConfigured();

        var familyId = "handle:" + _options.ProductFamilyHandle;
        var plans = new List<SubscriptionPlan>();
        const int perPage = 200;
        var page = 1;

        while (true)
        {
            IReadOnlyList<ProductResponse> batch;
            try
            {
                batch = await Bounded(
                    ct => _client.ProductFamilies.ListProductsForProductFamily(
                        productFamilyId: familyId,
                        dateField: null,
                        filter: null,
                        startDate: null,
                        endDate: null,
                        startDatetime: null,
                        endDatetime: null,
                        includeArchived: null,
                        include: null,
                        page: page,
                        perPage: perPage,
                        ct: ct),
                    cancellationToken);
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                throw TranslateListProductsError(ex);
            }
            catch (Exception ex) when (IsBoundaryException(ex))
            {
                throw TranslateBoundary(ex, "Unable to list subscription plans.");
            }

            if (batch.Count == 0)
            {
                break;
            }

            foreach (var envelope in batch)
            {
                if (envelope.Product is { } product && !string.IsNullOrWhiteSpace(product.Handle))
                {
                    _logger.LogDebug(
                        "Maxio product {Handle} requireCreditCard={RequireCreditCard} priceInCents={Price}",
                        product.Handle,
                        product.RequireCreditCard,
                        product.PriceInCents);
                    plans.Add(MapPlan(product));
                }
            }

            if (batch.Count < perPage)
            {
                break;
            }

            page++;
        }

        return plans;
    }

    public async Task<SubscribeResult> SubscribeAsync(ShopperIdentity shopper, string productHandle, CancellationToken cancellationToken)
    {
        EnsureConfigured();

        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new BillingProviderException(400, "A product handle is required.");
        }

        var gate = ShopperLocks.GetOrAdd(shopper.UserId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(shopper, cancellationToken);
            if (customer.Id is null)
            {
                throw new BillingProviderException(502, "The billing customer is missing an id.");
            }

            var existing = await FindCurrentSubscriptionAsync(customer, productHandle, cancellationToken);
            if (existing is not null)
            {
                return new SubscribeResult(existing, Created: false);
            }

            try
            {
                using (OnceOnlyWriteHandler.BeginWrite())
                {
                    var created = await Bounded(
                        ct => _client.Subscriptions.CreateSubscription(
                            body: new CreateSubscriptionRequest
                            {
                                Subscription = new CreateSubscription
                                {
                                    ProductHandle = productHandle,
                                    CustomerId = customer.Id.Value,
                                    PaymentCollectionMethod = CollectionMethod.Invoice
                                }
                            },
                            ct: ct),
                        cancellationToken);

                    var subscription = created.Subscription ?? throw UnreadableSuccess("The billing provider returned an empty subscription.");
                    return new SubscribeResult(MapSubscription(subscription), Created: true);
                }
            }
            catch (SdkException<CreateSubscriptionError> ex)
            {
                throw TranslateCreateSubscriptionError(ex, _logger);
            }
            catch (DuplicateWriteRefusedException ex)
            {
                return await RecoverSubscribeAsync(customer, productHandle, ex, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                return await RecoverSubscribeAsync(customer, productHandle, ex, cancellationToken);
            }
            catch (Exception ex) when (IsBoundaryException(ex))
            {
                throw TranslateBoundary(ex, "Unable to create the subscription.");
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<ShopperSubscription>> ListMySubscriptionsAsync(ShopperIdentity shopper, CancellationToken cancellationToken)
    {
        EnsureConfigured();

        var customer = await TryReadCustomerAsync(shopper.UserId, cancellationToken);
        if (customer?.Id is null)
        {
            return Array.Empty<ShopperSubscription>();
        }

        var rows = await ListCustomerSubscriptionsAsync(customer.Id.Value, cancellationToken);
        var result = new List<ShopperSubscription>();
        foreach (var envelope in rows)
        {
            if (envelope.Subscription is { } subscription)
            {
                result.Add(MapSubscription(subscription));
            }
        }

        return result;
    }

    private async Task<Customer> EnsureCustomerAsync(ShopperIdentity shopper, CancellationToken cancellationToken)
    {
        var existing = await TryReadCustomerAsync(shopper.UserId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            using (OnceOnlyWriteHandler.BeginWrite())
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
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                return await RecoverCustomerAsync(shopper.UserId, cancellationToken);
            }

            if (ex.Error.TryGetRawError(out var raw) && (int)raw.StatusCode == 422)
            {
                return await RecoverCustomerAsync(shopper.UserId, cancellationToken);
            }

            throw TranslateCreateCustomerError(ex);
        }
        catch (JsonException ex) when (LastStatusCaptureHandler.LastStatus == HttpStatusCode.UnprocessableEntity)
        {
            _logger.LogWarning(ex, "CreateCustomer 422 body could not be parsed; recovering by reference lookup.");
            return await RecoverCustomerAsync(shopper.UserId, cancellationToken);
        }
        catch (DuplicateWriteRefusedException)
        {
            return await RecoverCustomerAsync(shopper.UserId, cancellationToken);
        }
        catch (HttpRequestException)
        {
            var recovered = await TryReadCustomerAsync(shopper.UserId, cancellationToken);
            if (recovered is not null)
            {
                return recovered;
            }

            throw new BillingProviderException(503, "The billing provider could not be reached while creating the customer.", innerException: null);
        }
        catch (Exception ex) when (IsBoundaryException(ex))
        {
            throw TranslateBoundary(ex, "Unable to create the billing customer.");
        }
    }

    private async Task<Customer> RecoverCustomerAsync(string userId, CancellationToken cancellationToken)
    {
        var recovered = await TryReadCustomerAsync(userId, cancellationToken);
        if (recovered is not null)
        {
            return recovered;
        }

        throw new BillingProviderException(422, "The billing customer could not be created.");
    }

    private async Task<SubscribeResult> RecoverSubscribeAsync(Customer customer, string productHandle, Exception cause, CancellationToken cancellationToken)
    {
        _logger.LogWarning(cause, "Subscribe write outcome is unknown; reconciling against provider state.");
        var existing = await FindCurrentSubscriptionAsync(customer, productHandle, cancellationToken);
        if (existing is not null)
        {
            return new SubscribeResult(existing, Created: false);
        }

        throw new BillingProviderException(503, "The subscription request may have been sent. Confirm your subscriptions before retrying.", cause);
    }

    private async Task<Customer?> TryReadCustomerAsync(string userId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Bounded(
                ct => _client.Customers.ReadCustomerByReference(reference: userId, ct: ct),
                cancellationToken);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRaw(ex, "Unable to look up the billing customer.");
        }
        catch (Exception ex) when (IsBoundaryException(ex))
        {
            throw TranslateBoundary(ex, "Unable to look up the billing customer.");
        }
    }

    private async Task<ShopperSubscription?> FindCurrentSubscriptionAsync(Customer customer, string productHandle, CancellationToken cancellationToken)
    {
        if (customer.Id is null)
        {
            throw new BillingProviderException(502, "The billing customer is missing an id.");
        }

        var rows = await ListCustomerSubscriptionsAsync(customer.Id.Value, cancellationToken);
        foreach (var envelope in rows)
        {
            var subscription = envelope.Subscription;
            if (subscription is null)
            {
                continue;
            }

            var handle = subscription.Product?.Handle;
            if (!string.Equals(handle, productHandle, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (IsCurrentEnrollment(subscription.State))
            {
                return MapSubscription(subscription);
            }
        }

        return null;
    }

    private async Task<IReadOnlyList<SubscriptionResponse>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        try
        {
            return await Bounded(
                ct => _client.Customers.ListCustomerSubscriptions(customerId: customerId, ct: ct),
                cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRaw(ex, "Unable to list subscriptions.");
        }
        catch (Exception ex) when (IsBoundaryException(ex))
        {
            throw TranslateBoundary(ex, "Unable to list subscriptions.");
        }
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey)
            || string.IsNullOrWhiteSpace(_options.Subdomain)
            || string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw new BillingProviderException(503, "Subscription billing is not configured.");
        }
    }

    private static bool IsCurrentEnrollment(SubscriptionState? state)
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
            || state == SubscriptionState.OnHold
            || state == SubscriptionState.AwaitingSignup
            || state == SubscriptionState.Pending
            || state == SubscriptionState.Paused
            || state == SubscriptionState.Suspended;
    }

    private static SubscriptionPlan MapPlan(Product product)
    {
        return new SubscriptionPlan(
            product.Id,
            product.Handle ?? string.Empty,
            product.Name ?? string.Empty,
            product.Description,
            ToDollars(product.PriceInCents),
            product.Interval,
            product.IntervalUnit?.Value);
    }

    private static ShopperSubscription MapSubscription(Subscription subscription)
    {
        var product = subscription.Product;
        return new ShopperSubscription(
            subscription.Id,
            product?.Handle ?? string.Empty,
            product?.Name ?? string.Empty,
            ToDollars(subscription.ProductPriceInCents ?? product?.PriceInCents),
            subscription.State?.Value ?? string.Empty,
            subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt);
    }

    private static decimal ToDollars(long? priceInCents) => (priceInCents ?? 0) / 100m;

    private static bool IsBoundaryException(Exception ex) =>
        ex is JsonException or HttpRequestException or TaskCanceledException or OperationCanceledException;

    private BillingProviderException TranslateBoundary(Exception ex, string fallback)
    {
        if (ex is OperationCanceledException && ex is not TaskCanceledException)
        {
            throw ex;
        }

        if (ex is JsonException)
        {
            var status = LastStatusCaptureHandler.LastStatus;
            if (status is >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError)
            {
                _logger.LogWarning(ex, "Billing provider rejected the request but the error body could not be parsed.");
                return new BillingProviderException((int)status.Value, fallback, ex);
            }

            _logger.LogError(ex, "Billing provider returned a response that could not be processed.");
            return new BillingProviderException(502, "The billing provider returned a response that could not be processed.", ex);
        }

        if (ex is TaskCanceledException)
        {
            return new BillingProviderException(504, "The billing provider timed out.", ex);
        }

        return new BillingProviderException(503, "The billing provider could not be reached.", ex);
    }

    private static BillingProviderException TranslateListProductsError(SdkException<ListProductsForProductFamilyError> ex)
    {
        if (ex.Error.TryGetString(out _))
        {
            return new BillingProviderException(404, "The configured subscription catalog was not found.", ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return TranslateRaw(raw, "Unable to list subscription plans.", ex);
        }

        return new BillingProviderException(502, "Unable to list subscription plans.", ex);
    }

    private static BillingProviderException TranslateCreateCustomerError(SdkException<CreateCustomerError> ex)
    {
        if (ex.Error.TryGetCustomerErrorResponse1(out _))
        {
            return new BillingProviderException(422, "The billing customer could not be created.", ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return TranslateRaw(raw, "The billing customer could not be created.", ex);
        }

        return new BillingProviderException(502, "The billing customer could not be created.", ex);
    }

    private static BillingProviderException TranslateCreateSubscriptionError(
        SdkException<CreateSubscriptionError> ex,
        ILogger logger)
    {
        if (ex.Error.TryGetErrorListResponse1(out var list) && list.Errors is { Count: > 0 })
        {
            var detail = string.Join("; ", list.Errors);
            logger.LogWarning("CreateSubscription was rejected: {Errors}", detail);
            return new BillingProviderException(422, ClientSafeProviderMessage(detail, "The selected plan could not be used for a subscription."), ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            var rawBody = raw.ReadAsString();
            logger.LogWarning("CreateSubscription was rejected with HTTP {Status}: {Body}", (int)raw.StatusCode, rawBody);
            return TranslateRaw(raw, "Unable to create the subscription.", ex);
        }

        return new BillingProviderException(502, "Unable to create the subscription.", ex);
    }

    private static string ClientSafeProviderMessage(string detail, string fallback)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            return fallback;
        }

        if (detail.Contains("System.", StringComparison.Ordinal)
            || detail.Contains("JsonException", StringComparison.OrdinalIgnoreCase)
            || detail.Contains('\n'))
        {
            return fallback;
        }

        return detail.Length <= 300 ? detail : fallback;
    }

    private static BillingProviderException TranslateRaw(SdkException<RawError> ex, string fallback) =>
        TranslateRaw(ex.Error, fallback, ex);

    private static BillingProviderException TranslateRaw(RawError raw, string fallback, Exception inner)
    {
        var status = (int)raw.StatusCode;
        if (status is >= 400 and < 500)
        {
            return new BillingProviderException(status, fallback, inner);
        }

        return new BillingProviderException(status >= 500 ? status : 502, fallback, inner);
    }

    private static BillingProviderException UnreadableSuccess(string message) =>
        new(502, message);
}
