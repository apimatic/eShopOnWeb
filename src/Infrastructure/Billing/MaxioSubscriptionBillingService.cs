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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private const int ProductPageSize = 200;

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _enrollGates = new(StringComparer.Ordinal);

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
        var familyHandle = RequireFamilyHandle();
        var productFamilyId = "handle:" + familyHandle;
        var plans = new List<SubscriptionPlan>();
        var page = 1;

        while (true)
        {
            var batch = await Bounded(
                ct => GuardListProducts(() => _client.ProductFamilies.ListProductsForProductFamily(
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
                    ct: ct)),
                cancellationToken);

            foreach (var envelope in batch)
            {
                var product = envelope.Product;
                if (string.IsNullOrWhiteSpace(product.Handle))
                {
                    continue;
                }

                plans.Add(MapPlan(product, familyHandle));
            }

            if (batch.Count < ProductPageSize)
            {
                break;
            }

            page++;
        }

        return plans;
    }

    public async Task<SubscriptionEnrollment> EnrollAsync(
        ShopperIdentity shopper,
        string productHandle,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new BillingProviderException(400, "A product handle is required.");
        }

        var handle = productHandle.Trim();
        await EnsureProductInFamily(handle, cancellationToken);

        var customerReference = CustomerReferenceFor(shopper.UserId);
        var subscriptionReference = SubscriptionReferenceFor(shopper.UserId, handle);

        var gate = _enrollGates.GetOrAdd(subscriptionReference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await GetOrCreateCustomerAsync(shopper, customerReference, cancellationToken);

            var existing = await FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
            if (existing is not null)
            {
                return new SubscriptionEnrollment(existing, Created: false);
            }

            if (customer.Id is int customerId)
            {
                var listed = await ListCustomerSubscriptionsCoreAsync(customerId, cancellationToken);
                var match = listed.FirstOrDefault(s =>
                    string.Equals(s.ProductHandle, handle, StringComparison.OrdinalIgnoreCase)
                    && !IsTerminal(s.State));
                if (match is not null)
                {
                    return new SubscriptionEnrollment(match, Created: false);
                }
            }

            var collectionMethod = await ResolvePaymentCollectionMethodAsync(cancellationToken);
            try
            {
                var created = await CreateSubscriptionCoreAsync(
                    handle,
                    customer.Id,
                    customerReference,
                    subscriptionReference,
                    collectionMethod,
                    cancellationToken);

                var subscription = MapSubscription(created.Subscription)
                    ?? throw new BillingProviderException(502, "The billing provider returned a response that could not be processed.");

                _logger.LogInformation(
                    "Created Maxio subscription {SubscriptionId} for user {UserId} product {ProductHandle}",
                    subscription.Id, shopper.UserId, handle);

                return new SubscriptionEnrollment(subscription, Created: true);
            }
            catch (BillingProviderException ex) when (ex.StatusCode == 422)
            {
                var alternate = collectionMethod == CollectionMethod.Remittance
                    ? CollectionMethod.Invoice
                    : CollectionMethod.Remittance;
                _logger.LogWarning(
                    "CreateSubscription 422 with {CollectionMethod}, retrying with {Alternate}",
                    collectionMethod.Value, alternate.Value);

                try
                {
                    var created = await CreateSubscriptionCoreAsync(
                        handle,
                        customer.Id,
                        customerReference,
                        subscriptionReference,
                        alternate,
                        cancellationToken);

                    var subscription = MapSubscription(created.Subscription)
                        ?? throw new BillingProviderException(502, "The billing provider returned a response that could not be processed.");

                    return new SubscriptionEnrollment(subscription, Created: true);
                }
                catch (BillingProviderException)
                {
                    throw ex;
                }
            }
            catch (Exception ex) when (ex is MaxioWriteResendRefusedException or HttpRequestException or TaskCanceledException)
            {
                var recovered = await FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
                if (recovered is not null)
                {
                    return new SubscriptionEnrollment(recovered, Created: false);
                }

                throw MapTransport(ex);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<ShopperSubscription>> ListMySubscriptionsAsync(
        ShopperIdentity shopper,
        CancellationToken cancellationToken)
    {
        var customer = await ReadCustomerByReferenceAsync(CustomerReferenceFor(shopper.UserId), cancellationToken);
        if (customer?.Id is not int customerId)
        {
            return Array.Empty<ShopperSubscription>();
        }

        return await ListCustomerSubscriptionsCoreAsync(customerId, cancellationToken);
    }

    private async Task<SubscriptionResponse> CreateSubscriptionCoreAsync(
        string productHandle,
        int? customerId,
        string customerReference,
        string subscriptionReference,
        CollectionMethod collectionMethod,
        CancellationToken cancellationToken)
    {
        using (MaxioWriteGate.BeginWrite())
        {
            return await Bounded(
                ct => GuardCreateSubscription(() => _client.Subscriptions.CreateSubscription(
                    body: new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            ProductHandle = productHandle,
                            CustomerId = customerId,
                            CustomerReference = customerReference,
                            Reference = subscriptionReference,
                            PaymentCollectionMethod = collectionMethod
                        }
                    },
                    ct: ct)),
                cancellationToken);
        }
    }

    private CollectionMethod? _paymentCollectionMethod;

    private async Task<CollectionMethod> ResolvePaymentCollectionMethodAsync(CancellationToken cancellationToken)
    {
        if (_paymentCollectionMethod is not null)
        {
            return _paymentCollectionMethod;
        }

        try
        {
            var response = await Bounded(
                ct => GuardReadSite(() => _client.Sites.ReadSite(ct: ct)),
                cancellationToken);

            _paymentCollectionMethod = response.Site.RelationshipInvoicingEnabled == false
                ? CollectionMethod.Invoice
                : CollectionMethod.Remittance;
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning(ex, "Unable to read Maxio site collection architecture; defaulting to remittance.");
            _paymentCollectionMethod = CollectionMethod.Remittance;
        }

        return _paymentCollectionMethod;
    }

    private async Task<SiteResponse> GuardReadSite(Func<Task<SiteResponse>> call)
    {
        try
        {
            return await call();
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRaw(ex.Error, "Unable to read billing site settings.");
        }
        catch (Exception ex) when (IsBoundary(ex))
        {
            throw MapBoundary(ex, "Unable to read billing site settings.");
        }
    }

    private async Task EnsureProductInFamily(string productHandle, CancellationToken cancellationToken)
    {
        var plans = await ListPlansAsync(cancellationToken);
        if (!plans.Any(p => string.Equals(p.Handle, productHandle, StringComparison.OrdinalIgnoreCase)))
        {
            throw new BillingProviderException(400, $"Unknown subscription plan '{productHandle}'.");
        }
    }

    private async Task<Customer> GetOrCreateCustomerAsync(
        ShopperIdentity shopper,
        string customerReference,
        CancellationToken cancellationToken)
    {
        var existing = await ReadCustomerByReferenceAsync(customerReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            using (MaxioWriteGate.BeginWrite())
            {
                var created = await Bounded(
                    ct => GuardCreateCustomer(() => _client.Customers.CreateCustomer(
                        body: new CreateCustomerRequest
                        {
                            Customer = new CreateCustomer
                            {
                                FirstName = shopper.FirstName,
                                LastName = shopper.LastName,
                                Email = shopper.Email,
                                Reference = customerReference
                            }
                        },
                        ct: ct)),
                    cancellationToken);

                _logger.LogInformation("Created Maxio customer {CustomerId} for user {UserId}", created.Customer.Id, shopper.UserId);
                return created.Customer;
            }
        }
        catch (BillingProviderException ex) when (ex.StatusCode == 422)
        {
            var raced = await ReadCustomerByReferenceAsync(customerReference, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw;
        }
        catch (Exception ex) when (ex is MaxioWriteResendRefusedException or HttpRequestException or TaskCanceledException)
        {
            var recovered = await ReadCustomerByReferenceAsync(customerReference, cancellationToken);
            if (recovered is not null)
            {
                return recovered;
            }

            throw MapTransport(ex);
        }
    }

    private async Task<Customer?> ReadCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Bounded(
                ct => GuardReadCustomer(() => _client.Customers.ReadCustomerByReference(
                    reference: reference,
                    ct: ct)),
                cancellationToken);
            return response.Customer;
        }
        catch (BillingProviderException ex) when (ex.StatusCode == 404)
        {
            return null;
        }
    }

    private async Task<ShopperSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Bounded(
                ct => GuardFindSubscription(() => _client.Subscriptions.FindSubscription(
                    reference: reference,
                    ct: ct)),
                cancellationToken);
            return MapSubscription(response.Subscription);
        }
        catch (BillingProviderException ex) when (ex.StatusCode == 404)
        {
            return null;
        }
    }

    private async Task<IReadOnlyList<ShopperSubscription>> ListCustomerSubscriptionsCoreAsync(
        int customerId,
        CancellationToken cancellationToken)
    {
        var envelopes = await Bounded(
            ct => GuardListCustomerSubscriptions(() => _client.Customers.ListCustomerSubscriptions(
                customerId: customerId,
                ct: ct)),
            cancellationToken);

        return envelopes
            .Select(e => MapSubscription(e.Subscription))
            .Where(s => s is not null)
            .Cast<ShopperSubscription>()
            .ToList();
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private async Task<IReadOnlyList<ProductResponse>> GuardListProducts(Func<Task<IReadOnlyList<ProductResponse>>> call)
    {
        try
        {
            return await call();
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out var message))
            {
                _logger.LogWarning("Maxio list products 404: {Message}", message);
                throw new BillingProviderException(502, "Billing catalog is unavailable.", ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw MapRaw(raw, "Unable to list subscription plans.");
            }

            throw new BillingProviderException(502, "Unable to list subscription plans.", ex);
        }
        catch (Exception ex) when (IsBoundary(ex))
        {
            throw MapBoundary(ex, "Unable to list subscription plans.");
        }
    }

    private async Task<CustomerResponse> GuardReadCustomer(Func<Task<CustomerResponse>> call)
    {
        try
        {
            return await call();
        }
        catch (SdkException<RawError> ex)
        {
            if (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                throw new BillingProviderException(404, "Customer was not found.", ex);
            }

            throw MapRaw(ex.Error, "Unable to look up the billing customer.");
        }
        catch (Exception ex) when (IsBoundary(ex))
        {
            throw MapBoundary(ex, "Unable to look up the billing customer.");
        }
    }

    private async Task<CustomerResponse> GuardCreateCustomer(Func<Task<CustomerResponse>> call)
    {
        try
        {
            return await call();
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var body))
            {
                var detail = FormatCustomerErrors(body);
                throw new BillingProviderException(422, string.IsNullOrWhiteSpace(detail) ? "The billing customer could not be created." : detail, ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw MapRaw(raw, "The billing customer could not be created.");
            }

            throw new BillingProviderException(422, "The billing customer could not be created.", ex);
        }
        catch (Exception ex) when (ex is MaxioWriteResendRefusedException or HttpRequestException or TaskCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (IsBoundary(ex))
        {
            throw MapBoundary(ex, "The billing customer could not be created.");
        }
    }

    private async Task<SubscriptionResponse> GuardFindSubscription(Func<Task<SubscriptionResponse>> call)
    {
        try
        {
            return await call();
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out var noContent))
            {
                throw new BillingProviderException(404, "Subscription was not found.", ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                if (raw.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new BillingProviderException(404, "Subscription was not found.", ex);
                }

                throw MapRaw(raw, "Unable to look up the subscription.");
            }

            throw new BillingProviderException(502, "Unable to look up the subscription.", ex);
        }
        catch (Exception ex) when (IsBoundary(ex))
        {
            throw MapBoundary(ex, "Unable to look up the subscription.");
        }
    }

    private async Task<SubscriptionResponse> GuardCreateSubscription(Func<Task<SubscriptionResponse>> call)
    {
        try
        {
            return await call();
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var body))
            {
                var detail = body.Errors is { Count: > 0 } ? string.Join(" ", body.Errors) : null;
                throw new BillingProviderException(422, string.IsNullOrWhiteSpace(detail) ? "The subscription could not be created." : detail, ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw MapRaw(raw, "The subscription could not be created.");
            }

            throw new BillingProviderException(422, "The subscription could not be created.", ex);
        }
        catch (Exception ex) when (ex is MaxioWriteResendRefusedException or HttpRequestException or TaskCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (IsBoundary(ex))
        {
            throw MapBoundary(ex, "The subscription could not be created.");
        }
    }

    private async Task<IReadOnlyList<SubscriptionResponse>> GuardListCustomerSubscriptions(
        Func<Task<IReadOnlyList<SubscriptionResponse>>> call)
    {
        try
        {
            return await call();
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRaw(ex.Error, "Unable to list subscriptions.");
        }
        catch (Exception ex) when (IsBoundary(ex))
        {
            throw MapBoundary(ex, "Unable to list subscriptions.");
        }
    }

    private static bool IsBoundary(Exception ex) =>
        ex is JsonException or HttpRequestException or TaskCanceledException or MaxioWriteResendRefusedException;

    private BillingProviderException MapBoundary(Exception ex, string fallback)
    {
        if (ex is JsonException)
        {
            var status = MaxioLastHttp.Status;
            if (status is HttpStatusCode code && (int)code >= 400 && (int)code < 500)
            {
                return new BillingProviderException((int)code, "The billing request was rejected.", ex);
            }

            return new BillingProviderException(502, "The billing provider returned a response that could not be processed.", ex);
        }

        return MapTransport(ex, fallback);
    }

    private static BillingProviderException MapTransport(Exception ex, string fallback = "The billing provider is unreachable.")
    {
        if (ex is MaxioWriteResendRefusedException)
        {
            return new BillingProviderException(503, "The billing write could not be confirmed. Retry the request.", ex);
        }

        return new BillingProviderException(503, fallback, ex);
    }

    private BillingProviderException MapRaw(RawError raw, string fallback)
    {
        var status = (int)raw.StatusCode;
        if (status is 401 or 403)
        {
            _logger.LogError("Maxio rejected credentials with HTTP {StatusCode}", status);
            return new BillingProviderException(502, "Billing is temporarily unavailable.", null);
        }

        if (status >= 400 && status < 500)
        {
            return new BillingProviderException(status == 404 ? 404 : 400, fallback);
        }

        return new BillingProviderException(502, fallback);
    }

    private static string? FormatCustomerErrors(CustomerErrorResponse1 body)
    {
        if (body.Errors is null)
        {
            return null;
        }

        var parts = new List<string>();
        if (body.Errors.PerPage is { Count: > 0 })
        {
            parts.AddRange(body.Errors.PerPage);
        }

        if (body.Errors.PricePoint is { Count: > 0 })
        {
            parts.AddRange(body.Errors.PricePoint);
        }

        return parts.Count == 0 ? null : string.Join(" ", parts);
    }

    private string RequireFamilyHandle()
    {
        if (string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw new BillingProviderException(500, "Maxio:ProductFamilyHandle is not configured.");
        }

        return _options.ProductFamilyHandle.Trim();
    }

    public static string CustomerReferenceFor(string userId) => $"eshop-user:{userId}";

    public static string SubscriptionReferenceFor(string userId, string productHandle) =>
        $"eshop-sub:{userId}:{productHandle}";

    private static bool IsTerminal(string? state)
    {
        if (string.IsNullOrWhiteSpace(state))
        {
            return false;
        }

        return state is "canceled" or "expired" or "failed_to_create" or "trial_ended";
    }

    private static SubscriptionPlan MapPlan(Product product, string familyHandle) =>
        new(
            Handle: product.Handle!,
            Name: product.Name,
            PriceInCents: product.PriceInCents,
            Interval: product.Interval,
            IntervalUnit: product.IntervalUnit?.Value,
            RequireCreditCard: product.RequireCreditCard,
            ProductFamilyHandle: product.ProductFamily?.Handle ?? familyHandle);

    private static ShopperSubscription? MapSubscription(Subscription? subscription)
    {
        if (subscription is null)
        {
            return null;
        }

        return new ShopperSubscription(
            Id: subscription.Id,
            Reference: subscription.Reference,
            State: subscription.State?.Value,
            ProductHandle: subscription.Product?.Handle,
            ProductName: subscription.Product?.Name,
            ProductPriceInCents: subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents,
            CurrentBillingAmountInCents: subscription.CurrentBillingAmountInCents,
            NextAssessmentAt: subscription.NextAssessmentAt,
            CurrentPeriodEndsAt: subscription.CurrentPeriodEndsAt,
            CurrentPeriodStartedAt: subscription.CurrentPeriodStartedAt);
    }
}
