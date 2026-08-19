using System;
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
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
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

        var familyKey = "handle:" + _options.ProductFamilyHandle;
        var plans = new List<SubscriptionPlan>();
        const int perPage = 20;
        var page = 1;

        while (true)
        {
            IReadOnlyList<ProductResponse> batch;
            MaxioCallContext.Begin();
            try
            {
                batch = await _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: familyKey,
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
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                throw MapListProductsForProductFamily(ex);
            }
            catch (JsonException ex)
            {
                throw MapJson(ex);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                throw MapTransport(ex);
            }

            foreach (var item in batch)
            {
                var plan = MapPlan(item.Product);
                if (plan is not null)
                {
                    plans.Add(plan);
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

    public async Task<SubscriptionSummary> SubscribeAsync(SubscribeCommand command, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        if (string.IsNullOrWhiteSpace(command.ProductHandle))
        {
            throw new BillingException("A product handle is required.", 400, BillingFailureKind.ClientError);
        }

        var product = await ReadProductAsync(command.ProductHandle, cancellationToken);
        var familyHandle = product.ProductFamily?.Handle;
        if (!string.Equals(familyHandle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new BillingException("The requested plan is not available.", 400, BillingFailureKind.ClientError);
        }

        var customer = await EnsureCustomerAsync(command, cancellationToken);
        if (customer.Id is not int customerId)
        {
            throw new BillingException(
                "The billing provider returned a response that could not be processed.",
                502,
                BillingFailureKind.UnreadableSuccess);
        }

        var reference = $"{command.BuyerId}:{command.ProductHandle}";
        var existing = await TryFindSubscriptionAsync(reference, cancellationToken);
        if (existing is not null)
        {
            if (CanReactivate(existing.State))
            {
                return await ReactivateAsync(existing, cancellationToken);
            }

            return MapSubscription(existing);
        }

        return await CreateSubscriptionAsync(customerId, command.ProductHandle, reference, cancellationToken);
    }

    public async Task<IReadOnlyList<SubscriptionSummary>> ListSubscriptionsForBuyerAsync(
        string buyerId,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var customer = await TryReadCustomerByReferenceAsync(buyerId, cancellationToken);
        if (customer?.Id is not int customerId)
        {
            return Array.Empty<SubscriptionSummary>();
        }

        IReadOnlyList<SubscriptionResponse> envelopes;
        MaxioCallContext.Begin();
        try
        {
            envelopes = await _client.Customers.ListCustomerSubscriptions(
                customerId: customerId,
                ct: cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRaw(ex.Error);
        }
        catch (JsonException ex)
        {
            throw MapJson(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw MapTransport(ex);
        }

        var results = new List<SubscriptionSummary>(envelopes.Count);
        foreach (var envelope in envelopes)
        {
            if (envelope.Subscription is { } subscription)
            {
                results.Add(MapSubscription(subscription));
            }
        }

        return results;
    }

    private async Task<Product> ReadProductAsync(string handle, CancellationToken cancellationToken)
    {
        MaxioCallContext.Begin();
        try
        {
            var response = await _client.Products.ReadProductByHandle(
                apiHandle: handle,
                ct: cancellationToken);
            return response.Product;
        }
        catch (SdkException<RawError> ex)
        {
            if (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                throw new BillingException("The requested plan was not found.", 404, BillingFailureKind.NotFound);
            }

            throw FromRaw(ex.Error);
        }
        catch (JsonException ex)
        {
            throw MapJson(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw MapTransport(ex);
        }
    }

    private async Task<Customer> EnsureCustomerAsync(SubscribeCommand command, CancellationToken cancellationToken)
    {
        var existing = await TryReadCustomerByReferenceAsync(command.BuyerId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            using (MaxioCallContext.BeginWrite())
            {
                var created = await _client.Customers.CreateCustomer(
                    body: new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer
                        {
                            FirstName = command.FirstName,
                            LastName = command.LastName,
                            Email = command.Email,
                            Reference = command.BuyerId
                        }
                    },
                    ct: cancellationToken);
                return created.Customer;
            }
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            var raced = await TryReadCustomerByReferenceAsync(command.BuyerId, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw MapCreateCustomer(ex);
        }
        catch (DuplicateWriteRejectedException)
        {
            return await RequireCustomerAfterWriteAsync(command.BuyerId, cancellationToken);
        }
        catch (JsonException ex)
        {
            var raced = await TryReadCustomerByReferenceAsync(command.BuyerId, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw MapJson(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return await RequireCustomerAfterWriteAsync(command.BuyerId, cancellationToken, ex);
        }
    }

    private async Task<Customer> RequireCustomerAfterWriteAsync(
        string buyerId,
        CancellationToken cancellationToken,
        Exception? inner = null)
    {
        var customer = await TryReadCustomerByReferenceAsync(buyerId, cancellationToken);
        if (customer is not null)
        {
            return customer;
        }

        throw new BillingException(
            "The billing provider did not confirm the customer. Please retry.",
            502,
            BillingFailureKind.UnreadableSuccess,
            inner);
    }

    private async Task<Customer?> TryReadCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        MaxioCallContext.Begin();
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(
                reference: reference,
                ct: cancellationToken);
            return response.Customer;
        }
        catch (SdkException<RawError> ex)
        {
            if (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            throw FromRaw(ex.Error);
        }
        catch (JsonException ex)
        {
            throw MapJson(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw MapTransport(ex);
        }
    }

    private async Task<Subscription?> TryFindSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        MaxioCallContext.Begin();
        try
        {
            var response = await _client.Subscriptions.FindSubscription(
                reference: reference,
                ct: cancellationToken);
            return response.Subscription;
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out RawError _))
            {
                return null;
            }

            if (ex.Error.TryGetRawError(out RawError raw))
            {
                throw FromRaw(raw);
            }

            throw new BillingException(
                "The billing provider returned an error.",
                502,
                BillingFailureKind.ProviderError);
        }
        catch (JsonException ex)
        {
            throw MapJson(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw MapTransport(ex);
        }
    }

    private async Task<SubscriptionSummary> CreateSubscriptionAsync(
        int customerId,
        string productHandle,
        string reference,
        CancellationToken cancellationToken)
    {
        try
        {
            using (MaxioCallContext.BeginWrite())
            {
                var created = await _client.Subscriptions.CreateSubscription(
                    body: new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            ProductHandle = productHandle,
                            CustomerId = customerId,
                            Reference = reference,
                            PaymentCollectionMethod = CollectionMethod.Invoice
                        }
                    },
                    ct: cancellationToken);

                if (created.Subscription is null)
                {
                    throw new BillingException(
                        "The billing provider returned a response that could not be processed.",
                        502,
                        BillingFailureKind.UnreadableSuccess);
                }

                return MapSubscription(created.Subscription);
            }
        }
        catch (BillingException)
        {
            throw;
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            var existing = await TryFindSubscriptionAsync(reference, cancellationToken);
            if (existing is not null)
            {
                return MapSubscription(existing);
            }

            throw MapCreateSubscription(ex);
        }
        catch (DuplicateWriteRejectedException)
        {
            return await RequireSubscriptionAfterWriteAsync(reference, cancellationToken);
        }
        catch (JsonException ex)
        {
            var existing = await TryFindSubscriptionAsync(reference, cancellationToken);
            if (existing is not null)
            {
                return MapSubscription(existing);
            }

            throw MapJson(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return await RequireSubscriptionAfterWriteAsync(reference, cancellationToken, ex);
        }
    }

    private async Task<SubscriptionSummary> RequireSubscriptionAfterWriteAsync(
        string reference,
        CancellationToken cancellationToken,
        Exception? inner = null)
    {
        var existing = await TryFindSubscriptionAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return MapSubscription(existing);
        }

        throw new BillingException(
            "The billing provider did not confirm the subscription. Please retry.",
            502,
            BillingFailureKind.UnreadableSuccess,
            inner);
    }

    private async Task<SubscriptionSummary> ReactivateAsync(Subscription existing, CancellationToken cancellationToken)
    {
        if (existing.Id is not int subscriptionId)
        {
            throw new BillingException(
                "The billing provider returned a response that could not be processed.",
                502,
                BillingFailureKind.UnreadableSuccess);
        }

        MaxioCallContext.Begin();
        try
        {
            var response = await _client.SubscriptionStatus.ReactivateSubscription(
                subscriptionId: subscriptionId,
                body: new ReactivateSubscriptionRequest(),
                ct: cancellationToken);

            if (response.Subscription is null)
            {
                throw new BillingException(
                    "The billing provider returned a response that could not be processed.",
                    502,
                    BillingFailureKind.UnreadableSuccess);
            }

            return MapSubscription(response.Subscription);
        }
        catch (BillingException)
        {
            throw;
        }
        catch (SdkException<ReactivateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out ErrorListResponse1 list))
            {
                throw FromErrorList(list);
            }

            if (ex.Error.TryGetRawError(out RawError raw))
            {
                throw FromRaw(raw);
            }

            throw new BillingException(
                "The billing provider returned an error.",
                502,
                BillingFailureKind.ProviderError);
        }
        catch (JsonException ex)
        {
            throw MapJson(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw MapTransport(ex);
        }
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey)
            || string.IsNullOrWhiteSpace(_options.ProductFamilyHandle)
            || (string.IsNullOrWhiteSpace(_options.Subdomain) && string.IsNullOrWhiteSpace(_options.BaseUrl)))
        {
            throw new BillingException("Billing is not configured.", 503, BillingFailureKind.Unreachable);
        }
    }

    private static bool CanReactivate(SubscriptionState? state)
        => state == SubscriptionState.Canceled
           || state == SubscriptionState.TrialEnded
           || state == SubscriptionState.Unpaid;

    private static SubscriptionPlan? MapPlan(Product product)
    {
        if (string.IsNullOrWhiteSpace(product.Handle))
        {
            return null;
        }

        var cents = product.PriceInCents ?? 0;
        return new SubscriptionPlan
        {
            Handle = product.Handle,
            Name = product.Name ?? product.Handle,
            Description = product.Description,
            PriceInCents = cents,
            Price = cents / 100m,
            Interval = product.Interval ?? 1,
            IntervalUnit = product.IntervalUnit?.Value ?? IntervalUnit.Month.Value
        };
    }

    private static SubscriptionSummary MapSubscription(Subscription subscription)
    {
        var cents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? 0;
        return new SubscriptionSummary
        {
            Id = subscription.Id ?? 0,
            State = subscription.State?.Value ?? string.Empty,
            ProductHandle = subscription.Product?.Handle,
            ProductName = subscription.Product?.Name,
            Price = cents / 100m,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            Reference = subscription.Reference
        };
    }

    private BillingException MapListProductsForProductFamily(SdkException<ListProductsForProductFamilyError> ex)
    {
        if (ex.Error.TryGetString(out string _))
        {
            _logger.LogWarning("Maxio product family '{Family}' was not found.", _options.ProductFamilyHandle);
            return new BillingException(
                "The subscription catalog is not available.",
                503,
                BillingFailureKind.Unreachable);
        }

        if (ex.Error.TryGetRawError(out RawError raw))
        {
            return FromRaw(raw);
        }

        return new BillingException(
            "The billing provider returned an error.",
            502,
            BillingFailureKind.ProviderError);
    }

    private static BillingException MapCreateCustomer(SdkException<CreateCustomerError> ex)
    {
        if (ex.Error.TryGetCustomerErrorResponse1(out CustomerErrorResponse1 body))
        {
            var messages = new List<string>();
            if (body.Errors?.PerPage is { Count: > 0 } perPage)
            {
                messages.AddRange(perPage);
            }

            if (body.Errors?.PricePoint is { Count: > 0 } pricePoint)
            {
                messages.AddRange(pricePoint);
            }

            var detail = messages.Count > 0
                ? string.Join(" ", messages)
                : "The billing provider rejected the customer.";
            return new BillingException(detail, 422, BillingFailureKind.ClientError);
        }

        if (ex.Error.TryGetRawError(out RawError raw))
        {
            return FromRaw(raw);
        }

        return new BillingException(
            "The billing provider rejected the customer.",
            422,
            BillingFailureKind.ClientError);
    }

    private static BillingException MapCreateSubscription(SdkException<CreateSubscriptionError> ex)
    {
        if (ex.Error.TryGetErrorListResponse1(out ErrorListResponse1 list))
        {
            return FromErrorList(list);
        }

        if (ex.Error.TryGetRawError(out RawError raw))
        {
            return FromRaw(raw);
        }

        return new BillingException(
            "The billing provider rejected the subscription.",
            422,
            BillingFailureKind.ClientError);
    }

    private static BillingException FromErrorList(ErrorListResponse1 list)
    {
        var detail = list.Errors is { Count: > 0 }
            ? string.Join(" ", list.Errors)
            : "The billing provider rejected the request.";
        return new BillingException(detail, 422, BillingFailureKind.ClientError);
    }

    private static BillingException FromRaw(RawError raw)
    {
        var status = (int)raw.StatusCode;
        if (status is 401 or 403)
        {
            return new BillingException("Billing is unavailable.", 503, BillingFailureKind.Unreachable);
        }

        if (status == 404)
        {
            return new BillingException("The requested billing record was not found.", 404, BillingFailureKind.NotFound);
        }

        if (status is >= 400 and < 500)
        {
            return new BillingException("The billing provider rejected the request.", status, BillingFailureKind.ClientError);
        }

        return new BillingException("The billing provider returned an error.", status == 0 ? 502 : status, BillingFailureKind.ProviderError);
    }

    private static BillingException MapJson(JsonException ex)
    {
        if (MaxioCallContext.LastStatus is HttpStatusCode status && (int)status >= 400)
        {
            var code = (int)status;
            if (code is 401 or 403)
            {
                return new BillingException("Billing is unavailable.", 503, BillingFailureKind.Unreachable, ex);
            }

            if (code is >= 400 and < 500)
            {
                return new BillingException("The billing provider rejected the request.", code, BillingFailureKind.ClientError, ex);
            }

            return new BillingException("The billing provider returned an error.", code, BillingFailureKind.ProviderError, ex);
        }

        return new BillingException(
            "The billing provider returned a response that could not be processed.",
            502,
            BillingFailureKind.UnreadableSuccess,
            ex);
    }

    private static BillingException MapTransport(Exception ex)
        => new("Billing is unavailable.", 503, BillingFailureKind.Unreachable, ex);
}
