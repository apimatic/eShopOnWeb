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
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class MaxioBillingGateway : IMaxioBillingGateway
{
    private const int PageSize = 100;
    private const int MaximumPages = 100;
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(20);
    private readonly MaxioAdvancedBillingClient _client;
    private readonly ILogger<MaxioBillingGateway> _logger;
    private readonly MaxioOptions _options;

    public MaxioBillingGateway(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioOptions> options,
        ILogger<MaxioBillingGateway> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken)
    {
        try
        {
            var families = await BoundedAsync(
                ct => _client.ProductFamilies.ListProductFamilies(
                    dateField: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    ct: ct),
                cancellationToken);

            var matches = families
                .Select(x => x.ProductFamily)
                .Where(x => x is not null &&
                            x.ArchivedAt is null &&
                            string.Equals(x.Handle, _options.ProductFamilyHandle, StringComparison.Ordinal))
                .ToArray();

            if (matches.Length != 1 || matches[0]!.Id is null)
            {
                throw InvalidConfiguration(
                    "The configured Maxio product family could not be resolved uniquely.");
            }

            var familyId = matches[0]!.Id!.Value.ToString(CultureInfo.InvariantCulture);
            var products = new List<BillingPlan>();
            for (var page = 1; page <= MaximumPages; page++)
            {
                var response = await BoundedAsync(
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
                        perPage: PageSize,
                        ct: ct),
                    cancellationToken);

                products.AddRange(response
                    .Select(x => x.Product)
                    .Where(x => x.ArchivedAt is null && !string.IsNullOrWhiteSpace(x.Handle))
                    .Select(MapPlan));

                if (response.Count < PageSize)
                {
                    return products;
                }
            }

            throw InvalidResponse("Maxio product pagination exceeded the configured safety limit.");
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out _))
            {
                throw InvalidConfiguration("The configured Maxio product family was not found.", ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw ReadFailure(raw.StatusCode, ex);
            }

            throw InvalidResponse("Maxio returned an unreadable product catalog error.", ex);
        }
        catch (SdkException<RawError> ex)
        {
            throw ReadFailure(ex.Error.StatusCode, ex);
        }
        catch (JsonException ex)
        {
            throw InvalidResponse("Maxio returned a product catalog response that could not be processed.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw Unavailable(ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw Unavailable(ex);
        }
    }

    public async Task<BillingPlan?> FindPlanAsync(string productHandle, CancellationToken cancellationToken)
    {
        try
        {
            var response = await BoundedAsync(
                ct => _client.Products.ReadProductByHandle(productHandle, ct: ct),
                cancellationToken);
            var product = response.Product;
            if (!string.Equals(product.Handle, productHandle, StringComparison.Ordinal) ||
                product.ArchivedAt is not null ||
                !string.Equals(product.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.Ordinal))
            {
                return null;
            }

            return MapPlan(product);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw ReadFailure(ex.Error.StatusCode, ex);
        }
        catch (JsonException ex)
        {
            throw InvalidResponse("Maxio returned a plan response that could not be processed.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw Unavailable(ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw Unavailable(ex);
        }
    }

    public async Task<BillingCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await BoundedAsync(
                ct => _client.Customers.ReadCustomerByReference(reference, ct: ct),
                cancellationToken);
            return MapCustomer(response.Customer);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw ReadFailure(ex.Error.StatusCode, ex);
        }
        catch (JsonException ex)
        {
            throw InvalidResponse("Maxio returned a customer response that could not be processed.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw Unavailable(ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw Unavailable(ex);
        }
    }

    public async Task<BillingCustomer> CreateCustomerAsync(
        string reference,
        string firstName,
        string lastName,
        string email,
        CancellationToken cancellationToken)
    {
        var request = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference
            }
        };

        try
        {
            using var writeScope = MaxioWriteOnceHandler.BeginScope();
            var response = await BoundedAsync(
                ct => _client.Customers.CreateCustomer(request, ct: ct),
                cancellationToken);
            return MapCustomer(response.Customer);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                var existing = await FindCustomerAsync(reference, cancellationToken);
                if (existing is not null)
                {
                    return existing;
                }

                throw Rejected("Maxio rejected the customer profile required for this subscription.", ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw WriteFailure(raw.StatusCode, ex);
            }

            throw InvalidResponse("Maxio returned an unreadable customer-creation error.", ex);
        }
        catch (Exception ex) when (ex is MaxioWriteReplayBlockedException or HttpRequestException or JsonException)
        {
            return await ReconcileCustomerOrThrowUnknownAsync(reference, ex, cancellationToken);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            return await ReconcileCustomerOrThrowUnknownAsync(reference, ex, cancellationToken);
        }
    }

    public async Task<BillingSubscription?> FindSubscriptionAsync(
        string reference,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await BoundedAsync(
                ct => _client.Subscriptions.FindSubscription(reference, ct: ct),
                cancellationToken);
            return response.Subscription is null ? null : MapSubscription(response.Subscription);
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                return null;
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw ReadFailure(raw.StatusCode, ex);
            }

            throw InvalidResponse("Maxio returned an unreadable subscription lookup error.", ex);
        }
        catch (JsonException ex)
        {
            throw InvalidResponse("Maxio returned a subscription response that could not be processed.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw Unavailable(ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw Unavailable(ex);
        }
    }

    public async Task<BillingSubscription> CreateSubscriptionAsync(
        string productHandle,
        string customerReference,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        var paymentCollectionMethod = await ResolveNoCardCollectionMethodAsync(cancellationToken);
        var request = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = productHandle,
                CustomerReference = customerReference,
                Reference = subscriptionReference,
                PaymentCollectionMethod = paymentCollectionMethod
            }
        };

        try
        {
            using var writeScope = MaxioWriteOnceHandler.BeginScope();
            var response = await BoundedAsync(
                ct => _client.Subscriptions.CreateSubscription(request, ct: ct),
                cancellationToken);
            if (response.Subscription is null)
            {
                throw InvalidResponse("Maxio returned an empty subscription response.");
            }

            return MapSubscription(response.Subscription);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var error))
            {
                _logger.LogWarning(
                    "Maxio rejected CreateSubscription: {ProviderErrors}",
                    string.Join(" | ", error.Errors));
                throw Rejected("Maxio rejected the subscription request.", ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw WriteFailure(raw.StatusCode, ex);
            }

            throw InvalidResponse("Maxio returned an unreadable subscription-creation error.", ex);
        }
        catch (BillingProviderException)
        {
            throw;
        }
        catch (Exception ex) when (ex is MaxioWriteReplayBlockedException or HttpRequestException or JsonException)
        {
            return await ReconcileSubscriptionOrThrowUnknownAsync(subscriptionReference, ex, cancellationToken);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            return await ReconcileSubscriptionOrThrowUnknownAsync(subscriptionReference, ex, cancellationToken);
        }
    }

    private async Task<CollectionMethod> ResolveNoCardCollectionMethodAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await BoundedAsync(
                ct => _client.Sites.ReadSite(ct: ct),
                cancellationToken);

            return response.Site.RelationshipInvoicingEnabled == true
                ? CollectionMethod.Remittance
                : CollectionMethod.Invoice;
        }
        catch (SdkException<RawError> ex)
        {
            throw ReadFailure(ex.Error.StatusCode, ex);
        }
        catch (JsonException ex)
        {
            throw InvalidResponse("Maxio returned site settings that could not be processed.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw Unavailable(ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw Unavailable(ex);
        }
    }

    public async Task<IReadOnlyList<BillingSubscription>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await BoundedAsync(
                ct => _client.Customers.ListCustomerSubscriptions(customerId, ct: ct),
                cancellationToken);
            return response
                .Where(x => x.Subscription is not null)
                .Select(x => MapSubscription(x.Subscription!))
                .ToArray();
        }
        catch (SdkException<RawError> ex)
        {
            throw ReadFailure(ex.Error.StatusCode, ex);
        }
        catch (JsonException ex)
        {
            throw InvalidResponse("Maxio returned subscriptions that could not be processed.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw Unavailable(ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw Unavailable(ex);
        }
    }

    private async Task<BillingCustomer> ReconcileCustomerOrThrowUnknownAsync(
        string reference,
        Exception cause,
        CancellationToken cancellationToken)
    {
        try
        {
            var existing = await FindCustomerAsync(reference, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }
        }
        catch (BillingProviderException)
        {
            // The original write remains ambiguous; preserve that stronger safety signal.
        }

        throw UnknownWrite("The Maxio customer request is being reconciled.", cause);
    }

    private async Task<BillingSubscription> ReconcileSubscriptionOrThrowUnknownAsync(
        string reference,
        Exception cause,
        CancellationToken cancellationToken)
    {
        try
        {
            var existing = await FindSubscriptionAsync(reference, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }
        }
        catch (BillingProviderException)
        {
            // The original write remains ambiguous; preserve that stronger safety signal.
        }

        throw UnknownWrite("The Maxio subscription request is being reconciled.", cause);
    }

    private async Task<T> BoundedAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        return await operation(cts.Token);
    }

    private static BillingPlan MapPlan(Product product) =>
        new(
            Require(product.Handle, "product handle"),
            Require(product.Name, "product name"),
            product.Description,
            product.PriceInCents,
            product.Interval,
            product.IntervalUnit?.Value,
            product.ProductPricePointHandle);

    private static BillingCustomer MapCustomer(Customer customer)
    {
        if (customer.Id is null)
        {
            throw InvalidResponse("Maxio returned a customer without an ID.");
        }

        return new BillingCustomer(customer.Id.Value, Require(customer.Reference, "customer reference"));
    }

    private static BillingSubscription MapSubscription(Subscription subscription)
    {
        if (subscription.Id is null || subscription.Product is null)
        {
            throw InvalidResponse("Maxio returned an incomplete subscription.");
        }

        return new BillingSubscription(
            subscription.Id.Value,
            Require(subscription.Reference, "subscription reference"),
            Require(subscription.Product.Handle, "subscription product handle"),
            Require(subscription.Product.Name, "subscription product name"),
            subscription.ProductPriceInCents ?? subscription.Product.PriceInCents,
            subscription.Currency,
            subscription.State?.Value,
            subscription.NextAssessmentAt);
    }

    private static string Require(string? value, string field) =>
        !string.IsNullOrWhiteSpace(value)
            ? value
            : throw InvalidResponse($"Maxio returned a response without its {field}.");

    private static BillingProviderException ReadFailure(HttpStatusCode statusCode, Exception inner) =>
        statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            ? InvalidConfiguration("Maxio authentication or site configuration was rejected.", inner)
            : Unavailable(inner);

    private static BillingProviderException WriteFailure(HttpStatusCode statusCode, Exception inner) =>
        (int)statusCode is >= 400 and < 500
            ? Rejected("Maxio rejected the billing request.", inner)
            : UnknownWrite("The Maxio billing request is being reconciled.", inner);

    private static BillingProviderException Rejected(string message, Exception? inner = null) =>
        new(BillingFailureKind.Rejected, message, inner);

    private static BillingProviderException Unavailable(Exception? inner = null) =>
        new(BillingFailureKind.Unavailable, "Maxio is temporarily unavailable.", inner);

    private static BillingProviderException InvalidConfiguration(string message, Exception? inner = null) =>
        new(BillingFailureKind.Unavailable, message, inner);

    private static BillingProviderException InvalidResponse(string message, Exception? inner = null) =>
        new(BillingFailureKind.InvalidResponse, message, inner);

    private static BillingProviderException UnknownWrite(string message, Exception? inner = null) =>
        new(BillingFailureKind.UnknownWriteOutcome, message, inner);
}
