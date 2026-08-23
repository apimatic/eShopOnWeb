using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Subscriptions;

public sealed class MaxioRecurringSubscriptionService : IRecurringSubscriptionService
{
    private const int PageSize = 200;
    private static readonly TimeSpan TotalCallBudget = TimeSpan.FromSeconds(30);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly ISubscriptionBillingStore _store;
    private readonly ISubscriptionOperationLock _operationLock;
    private readonly MaxioSingleSendGuard _singleSendGuard;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioRecurringSubscriptionService> _logger;

    public MaxioRecurringSubscriptionService(
        MaxioAdvancedBillingClient client,
        ISubscriptionBillingStore store,
        ISubscriptionOperationLock operationLock,
        MaxioSingleSendGuard singleSendGuard,
        IOptions<MaxioOptions> options,
        ILogger<MaxioRecurringSubscriptionService> logger)
    {
        _client = client;
        _store = store;
        _operationLock = operationLock;
        _singleSendGuard = singleSendGuard;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var plans = new List<SubscriptionPlan>();
        for (var page = 1; ; page++)
        {
            IReadOnlyList<ProductResponse> response;
            try
            {
                response = await BoundedAsync(ct => _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: $"handle:{_options.ProductFamilyHandle}",
                    dateField: null,
                    filter: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    includeArchived: false,
                    include: null,
                    page: page,
                    perPage: PageSize,
                    ct: ct), cancellationToken);
            }
            catch (SdkException<ListProductsForProductFamilyError> exception)
            {
                throw MapListProductsError(exception);
            }
            catch (Exception exception) when (IsProviderBoundaryFailure(exception))
            {
                throw MapProviderFailure("Maxio plans could not be loaded.", exception, cancellationToken);
            }

            foreach (var envelope in response)
            {
                var product = envelope.Product;
                if (product.ArchivedAt is not null || string.IsNullOrWhiteSpace(product.Handle))
                {
                    continue;
                }

                plans.Add(new SubscriptionPlan(
                    product.Handle,
                    product.Name ?? product.Handle,
                    product.Description,
                    product.PriceInCents,
                    product.Interval,
                    product.IntervalUnit?.Value));
            }

            if (response.Count < PageSize)
            {
                break;
            }
        }

        return plans;
    }

    public async Task<SubscriptionDetails> SubscribeAsync(
        BillingUser user,
        string productHandle,
        CancellationToken cancellationToken)
    {
        productHandle = productHandle?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new BillingValidationException("A productHandle is required.");
        }

        await using var operation = await _operationLock.AcquireAsync(user.Id, productHandle, cancellationToken);

        var existing = await _store.FindSubscriptionAsync(user.Id, productHandle, cancellationToken);
        if (existing is not null)
        {
            return await ReconcileAsync(existing, cancellationToken);
        }

        var product = await ReadEligibleProductAsync(productHandle, cancellationToken);
        var reservation = await _store.GetOrCreateSubscriptionAsync(
            new RecurringSubscription(
                user.Id,
                product.Handle!,
                product.Name ?? product.Handle!,
                product.PriceInCents,
                $"eshop-sub-{Guid.NewGuid():N}"),
            cancellationToken);

        if (!reservation.Created)
        {
            return await ReconcileAsync(reservation.Subscription, cancellationToken);
        }

        var subscription = reservation.Subscription;
        var customer = await EnsureCustomerAsync(user, cancellationToken);
        var paymentCollectionMethod = await ReadPaymentCollectionMethodAsync(cancellationToken);

        subscription.MarkSendStarted();
        await _store.SaveSubscriptionAsync(subscription, cancellationToken);

        try
        {
            using var sendScope = _singleSendGuard.BeginSubscriptionCreate();
            var response = await BoundedAsync(ct => _client.Subscriptions.CreateSubscription(
                body: new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = product.Handle,
                        CustomerReference = customer.MaxioCustomerReference,
                        Reference = subscription.MaxioSubscriptionReference,
                        PaymentCollectionMethod = paymentCollectionMethod
                    }
                },
                ct: ct), cancellationToken);

            if (response.Subscription is null || response.Subscription.Id is null)
            {
                subscription.MarkForReconciliation();
                await _store.SaveSubscriptionAsync(subscription, cancellationToken);
                return ToDetails(subscription);
            }

            await ApplyRemoteAsync(subscription, response.Subscription, cancellationToken);
            return ToDetails(subscription);
        }
        catch (SdkException<CreateSubscriptionError> exception)
        {
            if (exception.Error.TryGetErrorListResponse1(out var validation))
            {
                subscription.Reject();
                await _store.SaveSubscriptionAsync(subscription, cancellationToken);
                var detail = validation.Errors.Count == 0
                    ? "Maxio rejected the subscription."
                    : string.Join(" ", validation.Errors);
                throw new BillingValidationException(detail);
            }

            if (exception.Error.TryGetRawError(out var raw))
            {
                if ((int)raw.StatusCode < 500)
                {
                    subscription.Reject();
                    await _store.SaveSubscriptionAsync(subscription, cancellationToken);
                    throw new BillingProviderException("Maxio rejected the subscription.", raw.StatusCode, exception);
                }
            }

            return await MarkAmbiguousAndReconcileAsync(subscription, exception, cancellationToken);
        }
        catch (Exception exception) when (IsAmbiguousWriteFailure(exception))
        {
            return await MarkAmbiguousAndReconcileAsync(subscription, exception, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<SubscriptionDetails>> ListForUserAsync(
        string applicationUserId,
        CancellationToken cancellationToken)
    {
        var subscriptions = await _store.ListSubscriptionsAsync(applicationUserId, cancellationToken);
        var result = new List<SubscriptionDetails>(subscriptions.Count);
        foreach (var subscription in subscriptions)
        {
            await using var operation = await _operationLock.AcquireAsync(
                applicationUserId,
                subscription.ProductHandle,
                cancellationToken);
            result.Add(await ReconcileAsync(subscription, cancellationToken));
        }

        return result;
    }

    private async Task<Product> ReadEligibleProductAsync(string productHandle, CancellationToken cancellationToken)
    {
        ProductResponse response;
        try
        {
            response = await BoundedAsync(
                ct => _client.Products.ReadProductByHandle(productHandle, ct: ct),
                cancellationToken);
        }
        catch (SdkException<RawError> exception) when (exception.Error.StatusCode == HttpStatusCode.NotFound)
        {
            throw new BillingValidationException("The requested subscription plan does not exist.");
        }
        catch (SdkException<RawError> exception)
        {
            throw new BillingProviderException("Maxio could not validate the subscription plan.", exception.Error.StatusCode, exception);
        }
        catch (Exception exception) when (IsProviderBoundaryFailure(exception))
        {
            throw MapProviderFailure("Maxio could not validate the subscription plan.", exception, cancellationToken);
        }

        var product = response.Product;
        if (product.ArchivedAt is not null ||
            string.IsNullOrWhiteSpace(product.Handle) ||
            !string.Equals(product.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.Ordinal))
        {
            throw new BillingValidationException("The requested product is not an available subscription plan.");
        }

        if (product.RequireCreditCard == true)
        {
            throw new BillingValidationException("The requested plan requires a payment method, which this subscribe flow does not collect.");
        }

        return product;
    }

    private async Task<MaxioCustomerMapping> EnsureCustomerAsync(BillingUser user, CancellationToken cancellationToken)
    {
        var mapping = await _store.GetOrCreateCustomerAsync(
            user.Id,
            BuildCustomerReference(user.Id),
            cancellationToken);
        if (mapping.MaxioCustomerId is not null)
        {
            return mapping;
        }

        var remote = await FindCustomerAsync(mapping.MaxioCustomerReference, cancellationToken);
        if (remote is null)
        {
            try
            {
                var response = await BoundedAsync(ct => _client.Customers.CreateCustomer(
                    body: new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer
                        {
                            FirstName = user.FirstName,
                            LastName = user.LastName,
                            Email = user.Email,
                            Reference = mapping.MaxioCustomerReference
                        }
                    },
                    ct: ct), cancellationToken);
                remote = response.Customer;
            }
            catch (SdkException<CreateCustomerError> exception)
            {
                remote = await FindCustomerAsync(mapping.MaxioCustomerReference, cancellationToken);
                if (remote is null)
                {
                    throw MapCreateCustomerError(exception);
                }
            }
            catch (Exception exception) when (IsProviderBoundaryFailure(exception))
            {
                mapping.MarkForReconciliation();
                await _store.SaveCustomerAsync(mapping, cancellationToken);
                remote = await FindCustomerAsync(mapping.MaxioCustomerReference, cancellationToken);
                if (remote is null)
                {
                    throw MapProviderFailure("The Maxio customer outcome is not yet known.", exception, cancellationToken);
                }
            }
        }

        if (remote.Id is null)
        {
            mapping.MarkForReconciliation();
            await _store.SaveCustomerAsync(mapping, cancellationToken);
            throw new BillingProviderException("Maxio returned a customer without an identifier.");
        }

        mapping.Confirm(remote.Id.Value);
        await _store.SaveCustomerAsync(mapping, cancellationToken);
        return mapping;
    }

    private async Task<CollectionMethod> ReadPaymentCollectionMethodAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await BoundedAsync(
                ct => _client.Sites.ReadSite(ct: ct),
                cancellationToken);

            return response.Site.RelationshipInvoicingEnabled switch
            {
                true => CollectionMethod.Remittance,
                false => CollectionMethod.Invoice,
                null => throw new BillingProviderException(
                    "Maxio did not report the site's billing architecture.")
            };
        }
        catch (SdkException<RawError> exception)
        {
            throw new BillingProviderException(
                "Maxio site billing settings could not be read.",
                exception.Error.StatusCode,
                exception);
        }
        catch (Exception exception) when (IsProviderBoundaryFailure(exception))
        {
            throw MapProviderFailure(
                "Maxio site billing settings could not be read.",
                exception,
                cancellationToken);
        }
    }

    private async Task<Customer?> FindCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await BoundedAsync(
                ct => _client.Customers.ReadCustomerByReference(reference, ct: ct),
                cancellationToken);
            return response.Customer;
        }
        catch (SdkException<RawError> exception) when (exception.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> exception)
        {
            throw new BillingProviderException("Maxio customer lookup failed.", exception.Error.StatusCode, exception);
        }
        catch (Exception exception) when (IsProviderBoundaryFailure(exception))
        {
            throw MapProviderFailure("Maxio customer lookup failed.", exception, cancellationToken);
        }
    }

    private async Task<SubscriptionDetails> ReconcileAsync(
        RecurringSubscription subscription,
        CancellationToken cancellationToken)
    {
        MaxioAdvancedBilling.Models.Subscription? remote = null;
        if (subscription.MaxioSubscriptionId is not null)
        {
            remote = await ReadSubscriptionAsync(subscription.MaxioSubscriptionId.Value, cancellationToken);
        }

        remote ??= await FindSubscriptionAsync(subscription.MaxioSubscriptionReference, cancellationToken);
        if (remote is not null && remote.Id is not null)
        {
            await ApplyRemoteAsync(subscription, remote, cancellationToken);
        }

        return ToDetails(subscription);
    }

    private async Task<SubscriptionDetails> MarkAmbiguousAndReconcileAsync(
        RecurringSubscription subscription,
        Exception exception,
        CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            exception,
            "Maxio subscription create has an ambiguous outcome for local reference {Reference}; reconciliation only will follow",
            subscription.MaxioSubscriptionReference);
        subscription.MarkForReconciliation();
        await _store.SaveSubscriptionAsync(subscription, cancellationToken);

        try
        {
            var remote = await FindSubscriptionAsync(subscription.MaxioSubscriptionReference, cancellationToken);
            if (remote is not null && remote.Id is not null)
            {
                await ApplyRemoteAsync(subscription, remote, cancellationToken);
            }
        }
        catch (BillingException reconciliationFailure)
        {
            _logger.LogWarning(
                reconciliationFailure,
                "Maxio subscription reconciliation is still pending for local reference {Reference}",
                subscription.MaxioSubscriptionReference);
        }

        return ToDetails(subscription);
    }

    private async Task<MaxioAdvancedBilling.Models.Subscription?> FindSubscriptionAsync(
        string reference,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await BoundedAsync(
                ct => _client.Subscriptions.FindSubscription(reference: reference, ct: ct),
                cancellationToken);
            return response.Subscription;
        }
        catch (SdkException<FindSubscriptionError> exception)
        {
            if (exception.Error.TryGetNoContent(out _))
            {
                return null;
            }

            if (exception.Error.TryGetRawError(out var raw))
            {
                throw new BillingProviderException("Maxio subscription lookup failed.", raw.StatusCode, exception);
            }

            throw new BillingProviderException("Maxio subscription lookup failed.", null, exception);
        }
        catch (Exception exception) when (IsProviderBoundaryFailure(exception))
        {
            throw MapProviderFailure("Maxio subscription lookup failed.", exception, cancellationToken);
        }
    }

    private async Task<MaxioAdvancedBilling.Models.Subscription?> ReadSubscriptionAsync(
        int subscriptionId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await BoundedAsync(
                ct => _client.Subscriptions.ReadSubscription(subscriptionId, include: null, ct: ct),
                cancellationToken);
            return response.Subscription;
        }
        catch (SdkException<RawError> exception) when (exception.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> exception)
        {
            throw new BillingProviderException("Maxio subscription refresh failed.", exception.Error.StatusCode, exception);
        }
        catch (Exception exception) when (IsProviderBoundaryFailure(exception))
        {
            throw MapProviderFailure("Maxio subscription refresh failed.", exception, cancellationToken);
        }
    }

    private async Task ApplyRemoteAsync(
        RecurringSubscription local,
        MaxioAdvancedBilling.Models.Subscription remote,
        CancellationToken cancellationToken)
    {
        if (remote.Id is null || remote.State is null)
        {
            local.MarkForReconciliation();
            await _store.SaveSubscriptionAsync(local, cancellationToken);
            return;
        }

        local.Confirm(
            remote.Id.Value,
            remote.Product?.Handle ?? local.ProductHandle,
            remote.Product?.Name ?? local.ProductName,
            remote.ProductPriceInCents ?? remote.CurrentBillingAmountInCents ?? remote.Product?.PriceInCents ?? local.PriceInCents,
            remote.Currency ?? local.Currency,
            remote.State.Value,
            remote.NextAssessmentAt ?? remote.CurrentPeriodEndsAt);
        await _store.SaveSubscriptionAsync(local, cancellationToken);
    }

    private static SubscriptionDetails ToDetails(RecurringSubscription subscription) =>
        new(
            subscription.MaxioSubscriptionReference,
            subscription.ProductHandle,
            subscription.ProductName,
            subscription.PriceInCents,
            subscription.Currency,
            subscription.ProviderState ?? "pending",
            subscription.NextBillingAt,
            subscription.OperationStatus is not SubscriptionOperationStatus.Confirmed);

    private async Task<T> BoundedAsync<T>(
        Func<CancellationToken, Task<T>> call,
        CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TotalCallBudget);
        return await call(budget.Token);
    }

    private static string BuildCustomerReference(string applicationUserId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(applicationUserId));
        return $"eshop-user-{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private static BillingProviderException MapListProductsError(SdkException<ListProductsForProductFamilyError> exception)
    {
        if (exception.Error.TryGetString(out _))
        {
            return new BillingProviderException("The configured Maxio product family was not found.", HttpStatusCode.NotFound, exception);
        }

        if (exception.Error.TryGetRawError(out var raw))
        {
            return new BillingProviderException("Maxio plans could not be loaded.", raw.StatusCode, exception);
        }

        return new BillingProviderException("Maxio plans could not be loaded.", null, exception);
    }

    private static BillingProviderException MapCreateCustomerError(SdkException<CreateCustomerError> exception)
    {
        if (exception.Error.TryGetCustomerErrorResponse1(out _))
        {
            return new BillingProviderException("Maxio rejected the customer profile.", HttpStatusCode.UnprocessableEntity, exception);
        }

        if (exception.Error.TryGetRawError(out var raw))
        {
            return new BillingProviderException("Maxio customer creation failed.", raw.StatusCode, exception);
        }

        return new BillingProviderException("Maxio customer creation failed.", null, exception);
    }

    private static bool IsProviderBoundaryFailure(Exception exception) =>
        exception is HttpRequestException or TaskCanceledException or JsonException;

    private static bool IsAmbiguousWriteFailure(Exception exception) =>
        exception is HttpRequestException or TaskCanceledException or JsonException or MaxioDuplicateSendBlockedException;

    private static BillingProviderException MapProviderFailure(
        string message,
        Exception exception,
        CancellationToken callerCancellation)
    {
        if (exception is TaskCanceledException && callerCancellation.IsCancellationRequested)
        {
            throw new OperationCanceledException(callerCancellation);
        }

        return new BillingProviderException(message, HttpStatusCode.ServiceUnavailable, exception);
    }
}
