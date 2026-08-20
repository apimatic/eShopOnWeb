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
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _subscribeLocks = new();

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
        var familyHandle = _options.ProductFamilyHandle;
        if (string.IsNullOrWhiteSpace(familyHandle))
        {
            throw new BillingException(500, "Maxio product family is not configured.");
        }

        var families = await InvokeAsync(
            ct => _client.ProductFamilies.ListProductFamilies(
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                ct: ct),
            cancellationToken,
            "Unable to list subscription catalogs.");

        var family = families
            .Select(envelope => envelope.ProductFamily)
            .FirstOrDefault(item => string.Equals(item?.Handle, familyHandle, StringComparison.OrdinalIgnoreCase));

        if (family?.Id is null)
        {
            throw new BillingException(404, "The configured subscription catalog was not found.");
        }

        var familyId = family.Id.Value.ToString();
        var plans = new List<SubscriptionPlan>();
        const int perPage = 20;
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
                        includeArchived: false,
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
                throw TranslateBoundary(ex, cancellationToken, "Unable to list subscription plans.");
            }

            foreach (var envelope in batch)
            {
                var product = envelope.Product;
                if (product.ArchivedAt is not null)
                {
                    continue;
                }

                plans.Add(MapPlan(product));
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
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new BillingException(400, "A product handle is required.");
        }

        var handle = productHandle.Trim();
        var gate = _subscribeLocks.GetOrAdd(shopper.Reference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            await ReadProductByHandleAsync(handle, cancellationToken);
            var customerId = await EnsureCustomerAsync(shopper, cancellationToken);

            var existing = await FindCurrentEnrollmentAsync(customerId, handle, cancellationToken);
            if (existing is not null)
            {
                return new SubscribeResult(existing, Created: false);
            }

            return await CreateSubscriptionAsync(shopper, customerId, handle, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<ShopperSubscription>> ListShopperSubscriptionsAsync(
        ShopperIdentity shopper,
        CancellationToken cancellationToken)
    {
        var customer = await TryReadCustomerByReferenceAsync(shopper.Reference, cancellationToken);
        if (customer?.Id is null)
        {
            return Array.Empty<ShopperSubscription>();
        }

        var envelopes = await ListCustomerSubscriptionsAsync(customer.Id.Value, cancellationToken);
        var result = new List<ShopperSubscription>();
        foreach (var envelope in envelopes)
        {
            if (envelope.Subscription is { } subscription)
            {
                result.Add(MapSubscription(subscription));
            }
        }

        return result;
    }

    private async Task<Product> ReadProductByHandleAsync(string handle, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Bounded(
                ct => _client.Products.ReadProductByHandle(apiHandle: handle, ct: ct),
                cancellationToken);
            return response.Product;
        }
        catch (SdkException<RawError> ex) when ((int)ex.Error.StatusCode == 404)
        {
            throw new BillingException(404, $"No subscription plan with handle '{handle}' was found.", ex);
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRaw(ex.Error, "Unable to resolve the subscription plan.");
        }
        catch (Exception ex) when (IsBoundaryException(ex))
        {
            throw TranslateBoundary(ex, cancellationToken, "Unable to resolve the subscription plan.");
        }
    }

    private async Task<int> EnsureCustomerAsync(ShopperIdentity shopper, CancellationToken cancellationToken)
    {
        var existing = await TryReadCustomerByReferenceAsync(shopper.Reference, cancellationToken);
        if (existing?.Id is int existingId)
        {
            return existingId;
        }

        try
        {
            using (MaxioWriteOnceScope.Arm())
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
                                Reference = shopper.Reference
                            }
                        },
                        ct: ct),
                    cancellationToken);

                if (created.Customer.Id is int createdId)
                {
                    return createdId;
                }
            }
        }
        catch (Exception ex) when (IsBoundaryException(ex) || ex is SdkException<CreateCustomerError> || ex is MaxioDuplicateWriteException)
        {
            var raced = await TryReadCustomerByReferenceAsync(shopper.Reference, cancellationToken);
            if (raced?.Id is int racedId)
            {
                return racedId;
            }

            if (ex is MaxioDuplicateWriteException)
            {
                throw new BillingException(502, "The billing provider request completed with an unknown outcome.", ex);
            }

            if (ex is SdkException<CreateCustomerError> createEx)
            {
                throw TranslateCreateCustomer(createEx);
            }

            throw TranslateBoundary(ex, cancellationToken, "Unable to create the billing customer.");
        }

        throw new BillingException(502, "The billing provider returned a response that could not be processed.");
    }

    private async Task<Customer?> TryReadCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Bounded(
                ct => _client.Customers.ReadCustomerByReference(reference: reference, ct: ct),
                cancellationToken);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when ((int)ex.Error.StatusCode == 404)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRaw(ex.Error, "Unable to look up the billing customer.");
        }
        catch (Exception ex) when (IsBoundaryException(ex))
        {
            throw TranslateBoundary(ex, cancellationToken, "Unable to look up the billing customer.");
        }
    }

    private async Task<SubscribeResult> CreateSubscriptionAsync(
        ShopperIdentity shopper,
        int customerId,
        string productHandle,
        CancellationToken cancellationToken)
    {
        try
        {
            using (MaxioWriteOnceScope.Arm())
            {
                var created = await Bounded(
                    ct => _client.Subscriptions.CreateSubscription(
                        body: new CreateSubscriptionRequest
                        {
                            Subscription = new CreateSubscription
                            {
                                ProductHandle = productHandle,
                                CustomerId = customerId,
                                Reference = $"{shopper.Reference}:{productHandle}",
                                PaymentCollectionMethod = CollectionMethod.Remittance
                            }
                        },
                        ct: ct),
                    cancellationToken);

                if (created.Subscription is null)
                {
                    var recovered = await FindCurrentEnrollmentAsync(customerId, productHandle, cancellationToken);
                    if (recovered is not null)
                    {
                        return new SubscribeResult(recovered, Created: true);
                    }

                    throw new BillingException(502, "The billing provider returned a response that could not be processed.");
                }

                return new SubscribeResult(MapSubscription(created.Subscription), Created: true);
            }
        }
        catch (Exception ex) when (IsBoundaryException(ex) || ex is SdkException<CreateSubscriptionError> || ex is MaxioDuplicateWriteException)
        {
            var recovered = await FindCurrentEnrollmentAsync(customerId, productHandle, cancellationToken);
            if (recovered is not null)
            {
                return new SubscribeResult(recovered, Created: false);
            }

            if (ex is MaxioDuplicateWriteException)
            {
                throw new BillingException(502, "The billing provider request completed with an unknown outcome.", ex);
            }

            if (ex is SdkException<CreateSubscriptionError> createEx)
            {
                throw TranslateCreateSubscription(createEx);
            }

            throw TranslateBoundary(ex, cancellationToken, "Unable to create the subscription.");
        }
    }

    private async Task<ShopperSubscription?> FindCurrentEnrollmentAsync(
        int customerId,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var envelopes = await ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        foreach (var envelope in envelopes)
        {
            var subscription = envelope.Subscription;
            if (subscription is null)
            {
                continue;
            }

            if (!string.Equals(subscription.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase))
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

    private async Task<IReadOnlyList<SubscriptionResponse>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await Bounded(
                ct => _client.Customers.ListCustomerSubscriptions(customerId: customerId, ct: ct),
                cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRaw(ex.Error, "Unable to list subscriptions.");
        }
        catch (Exception ex) when (IsBoundaryException(ex))
        {
            throw TranslateBoundary(ex, cancellationToken, "Unable to list subscriptions.");
        }
    }

    private async Task<T> InvokeAsync<T>(
        Func<CancellationToken, Task<T>> call,
        CancellationToken cancellationToken,
        string fallback)
    {
        try
        {
            return await Bounded(call, cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRaw(ex.Error, fallback);
        }
        catch (Exception ex) when (IsBoundaryException(ex))
        {
            throw TranslateBoundary(ex, cancellationToken, fallback);
        }
    }

    private static async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private static SubscriptionPlan MapPlan(Product product)
    {
        return new SubscriptionPlan(
            Handle: product.Handle ?? string.Empty,
            Name: product.Name ?? product.Handle ?? string.Empty,
            Price: (product.PriceInCents ?? 0) / 100m,
            Interval: product.Interval ?? 1,
            IntervalUnit: product.IntervalUnit?.Value ?? IntervalUnit.Month.Value);
    }

    private static ShopperSubscription MapSubscription(Subscription subscription)
    {
        if (subscription.Id is null)
        {
            throw new BillingException(502, "The billing provider returned a response that could not be processed.");
        }

        var priceCents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? 0;
        var handle = subscription.Product?.Handle ?? string.Empty;
        var name = subscription.Product?.Name ?? handle;

        return new ShopperSubscription(
            Id: subscription.Id.Value,
            PlanHandle: handle,
            PlanName: name,
            Price: priceCents / 100m,
            State: subscription.State?.Value ?? "unknown",
            NextBillingDate: subscription.NextAssessmentAt);
    }

    private static bool IsCurrentEnrollment(SubscriptionState? state)
    {
        if (state is null)
        {
            return true;
        }

        return state == SubscriptionState.Active
            || state == SubscriptionState.Assessing
            || state == SubscriptionState.Pending
            || state == SubscriptionState.Trialing
            || state == SubscriptionState.Paused
            || state == SubscriptionState.PastDue
            || state == SubscriptionState.SoftFailure
            || state == SubscriptionState.Unpaid
            || state == SubscriptionState.AwaitingSignup;
    }

    private static bool IsBoundaryException(Exception ex)
    {
        return ex is JsonException or HttpRequestException or OperationCanceledException;
    }

    private static BillingException TranslateBoundary(Exception ex, CancellationToken cancellationToken, string fallback)
    {
        if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
        {
            throw ex;
        }

        if (ex is JsonException json)
        {
            return FromJson(json);
        }

        if (ex is HttpRequestException)
        {
            return new BillingException(503, "The billing provider is unreachable.", ex);
        }

        if (ex is OperationCanceledException)
        {
            return new BillingException(504, "The billing provider request timed out.", ex);
        }

        return new BillingException(502, fallback, ex);
    }

    private static BillingException FromJson(JsonException ex)
    {
        var status = MaxioStatusCaptureHandler.LastStatus;
        if (status is >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError)
        {
            return new BillingException((int)status.Value, "The billing provider rejected the request.", ex);
        }

        return new BillingException(502, "The billing provider returned a response that could not be processed.", ex);
    }

    private static BillingException TranslateRaw(RawError raw, string fallback)
    {
        var status = (int)raw.StatusCode;
        if (status is 401 or 403)
        {
            return new BillingException(502, "The billing provider rejected the credentials.");
        }

        if (status is >= 400 and < 500)
        {
            return new BillingException(status, fallback);
        }

        return new BillingException(502, fallback);
    }

    private static BillingException TranslateListProductsError(SdkException<ListProductsForProductFamilyError> ex)
    {
        if (ex.Error.TryGetString(out _))
        {
            return new BillingException(404, "The configured subscription catalog was not found.", ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return TranslateRaw(raw, "Unable to list subscription plans.");
        }

        return new BillingException(502, "Unable to list subscription plans.", ex);
    }

    private BillingException TranslateCreateCustomer(SdkException<CreateCustomerError> ex)
    {
        if (ex.Error.TryGetCustomerErrorResponse1(out _))
        {
            return new BillingException(422, "The billing provider rejected the customer record.", ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            _logger.LogWarning("Maxio CreateCustomer failed with HTTP {Status}", (int)raw.StatusCode);
            return TranslateRaw(raw, "The billing provider rejected the customer record.");
        }

        return new BillingException(502, "The billing provider rejected the customer record.", ex);
    }

    private static BillingException TranslateCreateSubscription(SdkException<CreateSubscriptionError> ex)
    {
        if (ex.Error.TryGetErrorListResponse1(out var list))
        {
            var message = list.Errors is { Count: > 0 }
                ? string.Join(" ", list.Errors)
                : "The billing provider rejected the subscription.";
            return new BillingException(422, message, ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return TranslateRaw(raw, "The billing provider rejected the subscription.");
        }

        return new BillingException(502, "The billing provider rejected the subscription.", ex);
    }
}
