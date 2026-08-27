using System;
using System.Collections.Generic;
using System.Globalization;
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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

internal sealed class MaxioBillingGateway(
    MaxioAdvancedBillingClient client,
    IOptions<MaxioOptions> options,
    MaxioHttpCallContext callContext,
    ILogger<MaxioBillingGateway> logger) : IMaxioBillingGateway
{
    private static readonly TimeSpan TotalCallBudget = TimeSpan.FromSeconds(20);
    private const int PageSize = 100;
    private readonly MaxioOptions _options = options.Value;

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
    {
        _options.EnsureValid();
        IReadOnlyList<ProductFamilyResponse> families;
        try
        {
            families = await ExecuteAsync(false, ct => client.ProductFamilies.ListProductFamilies(
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                ct: ct), cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRaw(ex.Error, "Maxio could not return the subscription catalog.");
        }

        var family = families
            .Select(item => item.ProductFamily)
            .FirstOrDefault(item => string.Equals(item?.Handle, _options.ProductFamilyHandle,
                StringComparison.Ordinal));
        if (family?.Id is not int familyId)
        {
            throw new MaxioProviderException("The configured subscription catalog is unavailable.", 502);
        }

        var plans = new List<SubscriptionPlanDto>();
        for (var page = 1; ; page++)
        {
            IReadOnlyList<ProductResponse> products;
            try
            {
                products = await ExecuteAsync(false, ct => client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: familyId.ToString(CultureInfo.InvariantCulture),
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
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetString(out _))
                {
                    throw new MaxioProviderException("The configured subscription catalog is unavailable.", 404);
                }

                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw FromRaw(raw, "Maxio could not return the subscription catalog.");
                }

                throw new MaxioProviderException("Maxio could not return the subscription catalog.", 502);
            }

            foreach (var response in products)
            {
                var product = response.Product;
                if (product.ArchivedAt is not null || string.IsNullOrWhiteSpace(product.Handle) ||
                    string.IsNullOrWhiteSpace(product.Name) || product.PriceInCents is not long price ||
                    product.Interval is not int interval || product.IntervalUnit is null)
                {
                    continue;
                }

                plans.Add(new SubscriptionPlanDto(product.Handle, product.Name, product.Description,
                    price, interval, product.IntervalUnit.Value));
            }

            if (products.Count < PageSize) break;
        }

        return plans;
    }

    public async Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        _options.EnsureValid();
        try
        {
            var response = await ExecuteAsync(false,
                ct => client.Customers.ReadCustomerByReference(reference, ct: ct), cancellationToken);
            return response.Customer.Id is int id
                ? new MaxioCustomer(id)
                : throw MalformedSuccess();
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRaw(ex.Error, "Maxio could not resolve the billing customer.");
        }
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(
        BillingCustomerIdentity identity, CancellationToken cancellationToken)
    {
        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = identity.FirstName,
                LastName = identity.LastName,
                Email = identity.Email,
                Reference = identity.CustomerReference
            }
        };

        try
        {
            var response = await ExecuteAsync(true,
                ct => client.Customers.CreateCustomer(body, ct: ct), cancellationToken);
            return response.Customer.Id is int id
                ? new MaxioCustomer(id)
                : throw MalformedSuccess(true);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                throw new MaxioProviderException("Maxio rejected the billing customer.", 422);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRaw(raw, "Maxio rejected the billing customer.");
            }

            throw new MaxioProviderException("Maxio rejected the billing customer.", 422);
        }
    }

    public async Task<SubscriptionDto?> FindSubscriptionAsync(
        string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await ExecuteAsync(false,
                ct => client.Subscriptions.FindSubscription(reference: reference, ct: ct), cancellationToken);
            return response.Subscription is null ? throw MalformedSuccess() : Map(response.Subscription);
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out var notFound) && notFound.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                if (raw.StatusCode == HttpStatusCode.NotFound) return null;
                throw FromRaw(raw, "Maxio could not reconcile the subscription.");
            }

            throw new MaxioProviderException("Maxio could not reconcile the subscription.", 502);
        }
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(string productHandle, int customerId,
        string reference, CancellationToken cancellationToken)
    {
        var body = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = productHandle,
                CustomerId = customerId,
                Reference = reference,
                PaymentCollectionMethod = MaxioAdvancedBilling.Models.Enums.CollectionMethod.Remittance
            }
        };

        try
        {
            var response = await ExecuteAsync(true,
                ct => client.Subscriptions.CreateSubscription(body, ct: ct), cancellationToken);
            return response.Subscription is null ? throw MalformedSuccess(true) : Map(response.Subscription);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var error))
            {
                logger.LogWarning(
                    "Maxio rejected subscription creation with validation errors: {ProviderErrors}",
                    FormatProviderErrors(error.Errors));
                throw new MaxioProviderException("Maxio rejected the subscription.", 422);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRaw(raw, "Maxio rejected the subscription.");
            }

            throw new MaxioProviderException("Maxio rejected the subscription.", 422);
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetCustomerSubscriptionsAsync(
        int customerId, CancellationToken cancellationToken)
    {
        try
        {
            var responses = await ExecuteAsync(false,
                ct => client.Customers.ListCustomerSubscriptions(customerId, ct: ct), cancellationToken);
            return responses.Where(item => item.Subscription is not null)
                .Select(item => Map(item.Subscription!)).ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRaw(ex.Error, "Maxio could not return subscriptions.");
        }
    }

    private async Task<T> ExecuteAsync<T>(bool write, Func<CancellationToken, Task<T>> call,
        CancellationToken cancellationToken)
    {
        using var scope = callContext.Begin(write);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TotalCallBudget);
        try
        {
            return await call(budget.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (MaxioWriteReplayBlockedException ex)
        {
            throw new MaxioProviderException(
                "The subscription request is being reconciled with Maxio.", null, true, ex);
        }
        catch (TaskCanceledException ex)
        {
            throw new MaxioProviderException(
                write ? "The Maxio write outcome is being reconciled." : "Maxio did not respond in time.",
                null, write, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new MaxioProviderException(
                write ? "The Maxio write outcome is being reconciled." : "Maxio is temporarily unavailable.",
                null, write, ex);
        }
        catch (JsonException ex)
        {
            var status = callContext.LastStatusCode;
            var rejected = status is >= HttpStatusCode.BadRequest;
            throw new MaxioProviderException(
                rejected ? "Maxio rejected the request." : "Maxio returned a response that could not be processed.",
                rejected ? (int)status!.Value : null,
                write && !rejected,
                ex);
        }
    }

    private static MaxioProviderException FromRaw(RawError raw, string message) =>
        new(message, (int)raw.StatusCode);

    private static MaxioProviderException MalformedSuccess(bool write = false) =>
        new("Maxio returned an incomplete response.", null, write);

    private static string FormatProviderErrors(IReadOnlyList<string> errors) =>
        string.Join(" | ", errors.Take(10).Select(SanitizeProviderError));

    private static string SanitizeProviderError(string error)
    {
        const int maxLength = 300;
        var singleLine = error.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();
        return singleLine.Length <= maxLength ? singleLine : string.Concat(singleLine.AsSpan(0, maxLength), "…");
    }

    private static SubscriptionDto Map(Subscription subscription)
    {
        if (subscription.Id is not int id || subscription.Product is null ||
            string.IsNullOrWhiteSpace(subscription.Product.Handle) ||
            string.IsNullOrWhiteSpace(subscription.Product.Name) ||
            subscription.Product.Interval is not int interval || subscription.Product.IntervalUnit is null)
        {
            throw MalformedSuccess();
        }

        var price = subscription.ProductPriceInCents ?? subscription.Product.PriceInCents;
        if (price is null) throw MalformedSuccess();

        return new SubscriptionDto(
            id,
            subscription.Reference ?? string.Empty,
            subscription.Product.Handle,
            subscription.Product.Name,
            price.Value,
            subscription.Currency ?? string.Empty,
            subscription.State?.Value ?? "unknown",
            subscription.NextAssessmentAt,
            interval,
            subscription.Product.IntervalUnit.Value);
    }
}
