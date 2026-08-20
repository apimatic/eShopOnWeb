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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

internal sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private const int ProductsPerPage = 200;
    private const string PreferredDefaultHandle = "eshop-pro";

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

    public Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken)
        => Bounded(ct => ListPlansCoreAsync(ct), cancellationToken);

    public Task<ShopperSubscription> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken)
        => Bounded(ct => SubscribeCoreAsync(request, ct), cancellationToken);

    public Task<IReadOnlyList<ShopperSubscription>> ListMySubscriptionsAsync(string userId, CancellationToken cancellationToken)
        => Bounded(ct => ListMySubscriptionsCoreAsync(userId, ct), cancellationToken);

    private async Task<IReadOnlyList<SubscriptionPlan>> ListPlansCoreAsync(CancellationToken ct)
    {
        var familyHandle = _options.ProductFamilyHandle;
        var productFamilyId = "handle:" + familyHandle;
        var plans = new List<SubscriptionPlan>();

        try
        {
            var page = 1;
            while (true)
            {
                var products = await _client.ProductFamilies.ListProductsForProductFamily(
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
                    perPage: ProductsPerPage,
                    ct: ct);

                foreach (var envelope in products)
                {
                    var mapped = MapPlan(envelope.Product, familyHandle);
                    if (mapped is not null)
                    {
                        plans.Add(mapped);
                    }
                }

                if (products.Count < ProductsPerPage)
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
        catch (Exception ex) when (IsBoundaryFailure(ex))
        {
            throw MapBoundaryFailure(ex, "listing subscription plans");
        }

        return plans;
    }

    private async Task<ShopperSubscription> SubscribeCoreAsync(SubscribeRequest request, CancellationToken ct)
    {
        var productHandle = await ResolveProductHandleAsync(request.ProductHandle, ct);
        var customerId = await EnsureCustomerAsync(request, ct);

        var existing = await FindExistingSubscriptionAsync(request.UserId, productHandle, customerId, ct);
        if (existing is not null)
        {
            return ToShopperSubscription(existing, created: false);
        }

        try
        {
            using (OncePerWriteGate.Begin())
            {
                var created = await _client.Subscriptions.CreateSubscription(
                    body: new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            ProductHandle = productHandle,
                            CustomerId = customerId,
                            Reference = SubscriptionReference(request.UserId, productHandle),
                            PaymentCollectionMethod = CollectionMethod.Remittance
                        }
                    },
                    ct: ct);

                if (created.Subscription is null)
                {
                    throw new BillingProviderException(
                        "The billing provider returned a response that could not be processed.",
                        502);
                }

                return ToShopperSubscription(created.Subscription, created: true);
            }
        }
        catch (DuplicateWriteRefusedException ex)
        {
            _logger.LogWarning(ex, "CreateSubscription transport retry was refused; reconciling.");
            var recovered = await FindExistingSubscriptionAsync(request.UserId, productHandle, customerId, ct);
            if (recovered is not null)
            {
                return ToShopperSubscription(recovered, created: false);
            }

            throw new BillingProviderException(
                "The billing provider did not confirm the subscription. Please retry.",
                503,
                innerException: ex);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            var recovered = await FindExistingSubscriptionAsync(request.UserId, productHandle, customerId, ct);
            if (recovered is not null)
            {
                return ToShopperSubscription(recovered, created: false);
            }

            throw MapCreateSubscriptionError(ex);
        }
        catch (JsonException ex)
        {
            var recovered = await FindExistingSubscriptionAsync(request.UserId, productHandle, customerId, ct);
            if (recovered is not null)
            {
                return ToShopperSubscription(recovered, created: false);
            }

            throw MapBoundaryFailure(ex, "creating a subscription");
        }
        catch (Exception ex) when (IsBoundaryFailure(ex))
        {
            var recovered = await FindExistingSubscriptionAsync(request.UserId, productHandle, customerId, ct);
            if (recovered is not null)
            {
                return ToShopperSubscription(recovered, created: false);
            }

            throw MapBoundaryFailure(ex, "creating a subscription");
        }
    }

    private async Task<IReadOnlyList<ShopperSubscription>> ListMySubscriptionsCoreAsync(string userId, CancellationToken ct)
    {
        var customerId = await TryReadCustomerIdAsync(userId, ct);
        if (customerId is null)
        {
            return Array.Empty<ShopperSubscription>();
        }

        try
        {
            var envelopes = await _client.Customers.ListCustomerSubscriptions(
                customerId: customerId.Value,
                ct: ct);

            return envelopes
                .Select(e => e.Subscription)
                .Where(s => s is not null)
                .Select(s => ToShopperSubscription(s!, created: false))
                .ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRawError(ex.Error, "listing subscriptions");
        }
        catch (Exception ex) when (IsBoundaryFailure(ex))
        {
            throw MapBoundaryFailure(ex, "listing subscriptions");
        }
    }

    private async Task<string> ResolveProductHandleAsync(string? requestedHandle, CancellationToken ct)
    {
        var plans = await ListPlansCoreAsync(ct);
        if (plans.Count == 0)
        {
            throw new BillingProviderException("No subscription plans are available.", 404, isClientError: true);
        }

        if (string.IsNullOrWhiteSpace(requestedHandle))
        {
            var preferred = plans.FirstOrDefault(p =>
                string.Equals(p.Handle, PreferredDefaultHandle, StringComparison.OrdinalIgnoreCase));
            return (preferred ?? plans[0]).Handle;
        }

        var match = plans.FirstOrDefault(p =>
            string.Equals(p.Handle, requestedHandle, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            throw new BillingProviderException($"Unknown subscription plan '{requestedHandle}'.", 400, isClientError: true);
        }

        return match.Handle;
    }

    private async Task<int> EnsureCustomerAsync(SubscribeRequest request, CancellationToken ct)
    {
        var existingId = await TryReadCustomerIdAsync(request.UserId, ct);
        if (existingId is not null)
        {
            return existingId.Value;
        }

        try
        {
            using (OncePerWriteGate.Begin())
            {
                var created = await _client.Customers.CreateCustomer(
                    body: new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer
                        {
                            FirstName = request.FirstName,
                            LastName = request.LastName,
                            Email = request.Email,
                            Reference = request.UserId
                        }
                    },
                    ct: ct);

                var id = created.Customer.Id;
                if (id is null)
                {
                    throw new BillingProviderException(
                        "The billing provider returned a response that could not be processed.",
                        502);
                }

                return id.Value;
            }
        }
        catch (DuplicateWriteRefusedException ex)
        {
            _logger.LogWarning(ex, "CreateCustomer transport retry was refused; reconciling.");
            return await RequireCustomerAfterRaceAsync(request.UserId, ct, ex);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            var recovered = await TryReadCustomerIdAsync(request.UserId, ct);
            if (recovered is not null)
            {
                return recovered.Value;
            }

            throw MapCreateCustomerError(ex);
        }
        catch (JsonException ex)
        {
            var recovered = await TryReadCustomerIdAsync(request.UserId, ct);
            if (recovered is not null)
            {
                return recovered.Value;
            }

            throw MapBoundaryFailure(ex, "creating a customer");
        }
        catch (Exception ex) when (IsBoundaryFailure(ex))
        {
            var recovered = await TryReadCustomerIdAsync(request.UserId, ct);
            if (recovered is not null)
            {
                return recovered.Value;
            }

            throw MapBoundaryFailure(ex, "creating a customer");
        }
    }

    private async Task<int> RequireCustomerAfterRaceAsync(string userId, CancellationToken ct, Exception inner)
    {
        var recovered = await TryReadCustomerIdAsync(userId, ct);
        if (recovered is not null)
        {
            return recovered.Value;
        }

        throw new BillingProviderException(
            "The billing provider did not confirm the customer. Please retry.",
            503,
            innerException: inner);
    }

    private async Task<int?> TryReadCustomerIdAsync(string userId, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(
                reference: userId,
                ct: ct);
            return response.Customer.Id;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRawError(ex.Error, "looking up a customer");
        }
        catch (Exception ex) when (IsBoundaryFailure(ex))
        {
            throw MapBoundaryFailure(ex, "looking up a customer");
        }
    }

    private async Task<Subscription?> FindExistingSubscriptionAsync(
        string userId,
        string productHandle,
        int customerId,
        CancellationToken ct)
    {
        var byReference = await TryFindByReferenceAsync(SubscriptionReference(userId, productHandle), ct);
        if (byReference is not null && !IsEndOfLife(byReference.State))
        {
            return byReference;
        }

        try
        {
            var envelopes = await _client.Customers.ListCustomerSubscriptions(
                customerId: customerId,
                ct: ct);

            return envelopes
                .Select(e => e.Subscription)
                .FirstOrDefault(s =>
                    s is not null
                    && string.Equals(s.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase)
                    && !IsEndOfLife(s.State));
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRawError(ex.Error, "listing subscriptions");
        }
        catch (Exception ex) when (IsBoundaryFailure(ex))
        {
            throw MapBoundaryFailure(ex, "listing subscriptions");
        }
    }

    private async Task<Subscription?> TryFindByReferenceAsync(string reference, CancellationToken ct)
    {
        try
        {
            var found = await _client.Subscriptions.FindSubscription(
                reference: reference,
                ct: ct);
            return found.Subscription;
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                return null;
            }

            if (ex.Error.TryGetRawError(out var raw) && raw.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            throw MapFindSubscriptionError(ex);
        }
        catch (Exception ex) when (IsBoundaryFailure(ex))
        {
            throw MapBoundaryFailure(ex, "finding a subscription");
        }
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private static string SubscriptionReference(string userId, string productHandle) =>
        $"{userId}:{productHandle}";

    private static SubscriptionPlan? MapPlan(Product product, string familyHandle)
    {
        if (product.ArchivedAt is not null)
        {
            return null;
        }

        if (product.ProductFamily?.Handle is string family
            && !string.Equals(family, familyHandle, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(product.Handle) || string.IsNullOrWhiteSpace(product.Name))
        {
            return null;
        }

        return new SubscriptionPlan(
            product.Handle,
            product.Name,
            CentsToAmount(product.PriceInCents),
            product.Interval ?? 1,
            product.IntervalUnit?.Value ?? IntervalUnit.Month.Value);
    }

    private static ShopperSubscription ToShopperSubscription(Subscription subscription, bool created)
    {
        if (subscription.Id is null)
        {
            throw new BillingProviderException(
                "The billing provider returned a response that could not be processed.",
                502);
        }

        var nextBilling = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt;
        return new ShopperSubscription(
            subscription.Id.Value,
            subscription.Product?.Handle ?? string.Empty,
            subscription.Product?.Name ?? string.Empty,
            CentsToAmount(subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents),
            subscription.State?.Value ?? string.Empty,
            nextBilling,
            created);
    }

    private static decimal CentsToAmount(long? cents) =>
        cents is null ? 0m : cents.Value / 100m;

    private static bool IsEndOfLife(SubscriptionState? state) =>
        state == SubscriptionState.Canceled
        || state == SubscriptionState.Expired
        || state == SubscriptionState.FailedToCreate
        || state == SubscriptionState.TrialEnded;

    private BillingProviderException MapListProductsError(SdkException<ListProductsForProductFamilyError> ex)
    {
        if (ex.Error.TryGetString(out _))
        {
            return new BillingProviderException("Subscription plans were not found.", 404, isClientError: true, ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return MapRawError(raw, "listing subscription plans");
        }

        return new BillingProviderException("The billing provider rejected the request.", 502, innerException: ex);
    }

    private BillingProviderException MapCreateCustomerError(SdkException<CreateCustomerError> ex)
    {
        if (ex.Error.TryGetCustomerErrorResponse1(out _))
        {
            return new BillingProviderException("The billing request was rejected.", 422, isClientError: true, ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return MapRawError(raw, "creating a customer");
        }

        return new BillingProviderException("The billing request was rejected.", 422, isClientError: true, ex);
    }

    private BillingProviderException MapCreateSubscriptionError(SdkException<CreateSubscriptionError> ex)
    {
        if (ex.Error.TryGetErrorListResponse1(out var list))
        {
            var detail = list.Errors.FirstOrDefault(e => !string.IsNullOrWhiteSpace(e));
            var message = string.IsNullOrWhiteSpace(detail)
                ? "The billing request was rejected."
                : detail;
            return new BillingProviderException(message, 422, isClientError: true, ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return MapRawError(raw, "creating a subscription");
        }

        return new BillingProviderException("The billing request was rejected.", 422, isClientError: true, ex);
    }

    private BillingProviderException MapFindSubscriptionError(SdkException<FindSubscriptionError> ex)
    {
        if (ex.Error.TryGetNoContent(out var noContent))
        {
            return MapRawError(noContent, "finding a subscription");
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return MapRawError(raw, "finding a subscription");
        }

        return new BillingProviderException("The billing provider rejected the request.", 502, innerException: ex);
    }

    private BillingProviderException MapRawError(RawError raw, string operation)
    {
        var status = (int)raw.StatusCode;
        _logger.LogWarning("Maxio {Operation} failed with HTTP {StatusCode}", operation, status);

        if (status is >= 400 and < 500)
        {
            var message = status == 404
                ? "The requested billing resource was not found."
                : "The billing request was rejected.";
            return new BillingProviderException(message, status, isClientError: true);
        }

        return new BillingProviderException("The billing provider is unavailable.", 503);
    }

    private static bool IsBoundaryFailure(Exception ex) =>
        ex is JsonException or HttpRequestException or TaskCanceledException or OperationCanceledException;

    private BillingProviderException MapBoundaryFailure(Exception ex, string operation)
    {
        if (ex is OperationCanceledException && ex is not TaskCanceledException)
        {
            throw ex;
        }

        if (ex is JsonException)
        {
            var captured = MaxioLastResponse.Code;
            if (captured is >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError)
            {
                _logger.LogWarning(ex, "Maxio {Operation} returned an unreadable {StatusCode} body", operation, (int)captured);
                return new BillingProviderException("The billing request was rejected.", (int)captured.Value, isClientError: true, ex);
            }

            _logger.LogWarning(ex, "Maxio {Operation} returned an unreadable success body", operation);
            return new BillingProviderException(
                "The billing provider returned a response that could not be processed.",
                502,
                innerException: ex);
        }

        _logger.LogWarning(ex, "Maxio {Operation} failed at the transport layer", operation);
        return new BillingProviderException("The billing provider is unavailable.", 503, innerException: ex);
    }
}
