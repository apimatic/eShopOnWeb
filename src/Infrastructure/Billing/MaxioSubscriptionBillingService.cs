using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Maxio;
using Maxio.Core;
using Maxio.Core.ErrorResponse;
using Maxio.Core.Exceptions;
using Maxio.Core.Hooks;
using Maxio.Errors;
using Maxio.Models;
using Maxio.Models.Enums;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionBilling;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private const int MaxProductPages = 10;
    private const int ProductsPerPage = 200;
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(45);

    private readonly MaxioClient _client;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;
    private static readonly AsyncLocal<HttpStatusCode?> LastStatus = new();

    public MaxioSubscriptionBillingService(
        MaxioClient client,
        IOptions<MaxioOptions> options,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken)
    {
        using var cts = LinkedDeadline(cancellationToken);
        var ct = cts.Token;
        var plans = new List<SubscriptionPlan>();
        var familyId = $"handle:{_options.ProductFamilyHandle}";

        try
        {
            for (var page = 1; page <= MaxProductPages; page++)
            {
                IReadOnlyList<ProductResponse> batch;
                try
                {
                    batch = await _client.ProductFamilies.ListProductsForProductFamily(
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
                        perPage: ProductsPerPage,
                        requestOptions: Observe(),
                        ct: ct);
                }
                catch (SdkException<ListProductsForProductFamilyError> ex)
                {
                    throw TranslateListProductsError(ex);
                }

                foreach (var envelope in batch)
                {
                    var product = envelope.Product;
                    if (product.ArchivedAt is not null)
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(product.Handle) || string.IsNullOrWhiteSpace(product.Name))
                    {
                        continue;
                    }

                    plans.Add(MapPlan(product));
                }

                if (batch.Count < ProductsPerPage)
                {
                    break;
                }

                if (page == MaxProductPages)
                {
                    _logger.LogWarning("Stopped listing Maxio plans after {MaxPages} pages for family {Family}", MaxProductPages, _options.ProductFamilyHandle);
                }
            }
        }
        catch (MaxioBillingException)
        {
            throw;
        }
        catch (Exception ex) when (IsBoundaryException(ex))
        {
            throw TranslateBoundary(ex);
        }

        _logger.LogInformation("Listed {Count} Maxio subscription plans for family {Family}", plans.Count, _options.ProductFamilyHandle);
        return plans;
    }

    public async Task<ShopperSubscription> SubscribeAsync(string customerReference, string email, string productHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new MaxioBillingException("A product handle is required.", 400, isCallerFault: true);
        }

        productHandle = productHandle.Trim();
        using var cts = LinkedDeadline(cancellationToken);
        var ct = cts.Token;
        var subscriptionReference = $"{customerReference}:{productHandle}";

        try
        {
            await EnsureProductExists(productHandle, ct);

            var customerId = await EnsureCustomer(customerReference, email, ct);

            var existing = await TryFindSubscription(subscriptionReference, ct);
            if (existing is not null)
            {
                _logger.LogInformation("Returning existing Maxio subscription {SubscriptionId} for {Reference}", existing.Id, subscriptionReference);
                return existing with { AlreadyExisted = true };
            }

            SubscriptionResponse created;
            try
            {
                created = await _client.Subscriptions.CreateSubscription(
                    body: new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            ProductHandle = productHandle,
                            CustomerId = customerId,
                            Reference = subscriptionReference,
                            // Catalog products do not require a card; automatic collection still
                            // attempts the first period immediately and 422s without a profile.
                            PaymentCollectionMethod = CollectionMethod.Remittance
                        }
                    },
                    requestOptions: Observe(),
                    ct: ct);
            }
            catch (SdkException<CreateSubscriptionError> ex)
            {
                if (ex.Error.TryGetErrorListResponse1(out var list))
                {
                    var replay = await TryFindSubscription(subscriptionReference, ct);
                    if (replay is not null)
                    {
                        _logger.LogWarning("CreateSubscription 422 reconciled to existing subscription {SubscriptionId}", replay.Id);
                        return replay with { AlreadyExisted = true };
                    }

                    throw new MaxioBillingException(FormatErrorList(list), 422, isCallerFault: true, ex);
                }

                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw FromRaw("Unable to create the subscription.", raw, ex);
                }

                throw new MaxioBillingException("Unable to create the subscription.", innerException: ex);
            }

            var mapped = MapSubscription(created, alreadyExisted: false);
            _logger.LogInformation("Created Maxio subscription {SubscriptionId} for {Reference} product {ProductHandle} state {State}", mapped.Id, subscriptionReference, mapped.ProductHandle, mapped.State);
            return mapped;
        }
        catch (MaxioBillingException)
        {
            throw;
        }
        catch (Exception ex) when (IsBoundaryException(ex))
        {
            throw TranslateBoundary(ex);
        }
    }

    public async Task<IReadOnlyList<ShopperSubscription>> ListMySubscriptionsAsync(string customerReference, CancellationToken cancellationToken)
    {
        using var cts = LinkedDeadline(cancellationToken);
        var ct = cts.Token;

        try
        {
            var customer = await TryReadCustomer(customerReference, ct);
            if (customer?.Id is null)
            {
                return Array.Empty<ShopperSubscription>();
            }

            IReadOnlyList<SubscriptionResponse> envelopes;
            try
            {
                envelopes = await _client.Customers.ListCustomerSubscriptions(
                    customerId: customer.Id.Value,
                    requestOptions: Observe(),
                    ct: ct);
            }
            catch (SdkException<RawError> ex)
            {
                throw FromRaw("Unable to list subscriptions.", ex.Error, ex);
            }

            var result = new List<ShopperSubscription>(envelopes.Count);
            foreach (var envelope in envelopes)
            {
                if (envelope.Subscription is null)
                {
                    continue;
                }

                result.Add(MapSubscription(envelope, alreadyExisted: true));
            }

            _logger.LogInformation("Listed {Count} Maxio subscriptions for customer {CustomerId}", result.Count, customer.Id);
            return result;
        }
        catch (MaxioBillingException)
        {
            throw;
        }
        catch (Exception ex) when (IsBoundaryException(ex))
        {
            throw TranslateBoundary(ex);
        }
    }

    private async Task EnsureProductExists(string productHandle, CancellationToken ct)
    {
        try
        {
            var response = await _client.Products.ReadProductByHandle(
                apiHandle: productHandle,
                requestOptions: Observe(),
                ct: ct);
            if (string.IsNullOrWhiteSpace(response.Product.Handle))
            {
                throw new MaxioBillingException("The provider returned a response that could not be processed.");
            }
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            throw new MaxioBillingException("Unknown subscription plan.", 404, isCallerFault: true, ex);
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRaw("Unable to read the subscription plan.", ex.Error, ex);
        }
    }

    private async Task<int> EnsureCustomer(string customerReference, string email, CancellationToken ct)
    {
        var existing = await TryReadCustomer(customerReference, ct);
        if (existing?.Id is int existingId)
        {
            return existingId;
        }

        var (firstName, lastName) = SplitName(email);
        try
        {
            var created = await _client.Customers.CreateCustomer(
                body: new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = firstName,
                        LastName = lastName,
                        Email = email,
                        Reference = customerReference
                    }
                },
                requestOptions: Observe(),
                ct: ct);

            if (created.Customer.Id is not int createdId)
            {
                throw new MaxioBillingException("The provider returned a response that could not be processed.");
            }

            _logger.LogInformation("Created Maxio customer {CustomerId} for reference {Reference}", createdId, customerReference);
            return createdId;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                var replay = await TryReadCustomer(customerReference, ct);
                if (replay?.Id is int replayId)
                {
                    _logger.LogWarning("CreateCustomer 422 reconciled to existing customer {CustomerId}", replayId);
                    return replayId;
                }

                throw new MaxioBillingException(FormatCustomerError(ex.Error), 422, isCallerFault: true, ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRaw("Unable to create the billing customer.", raw, ex);
            }

            throw new MaxioBillingException("Unable to create the billing customer.", innerException: ex);
        }
    }

    private async Task<Customer?> TryReadCustomer(string customerReference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(
                reference: customerReference,
                requestOptions: Observe(),
                ct: ct);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRaw("Unable to look up the billing customer.", ex.Error, ex);
        }
    }

    private async Task<ShopperSubscription?> TryFindSubscription(string subscriptionReference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Subscriptions.FindSubscription(
                reference: subscriptionReference,
                requestOptions: Observe(),
                ct: ct);
            if (response.Subscription is null)
            {
                throw new MaxioBillingException("The provider returned a response that could not be processed.");
            }

            return MapSubscription(response, alreadyExisted: true);
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                return null;
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRaw("Unable to look up the subscription.", raw, ex);
            }

            throw new MaxioBillingException("Unable to look up the subscription.", innerException: ex);
        }
    }

    private static SubscriptionPlan MapPlan(Product product) =>
        new()
        {
            Handle = product.Handle!,
            Name = product.Name!,
            Description = product.Description,
            Price = ToDollars(product.PriceInCents),
            Interval = product.Interval ?? 1,
            IntervalUnit = product.IntervalUnit?.Value ?? "month"
        };

    private static ShopperSubscription MapSubscription(SubscriptionResponse envelope, bool alreadyExisted)
    {
        var subscription = envelope.Subscription
            ?? throw new MaxioBillingException("The provider returned a response that could not be processed.");

        if (subscription.Id is not int id)
        {
            throw new MaxioBillingException("The provider returned a response that could not be processed.");
        }

        return new ShopperSubscription
        {
            Id = id,
            ProductHandle = subscription.Product?.Handle ?? string.Empty,
            ProductName = subscription.Product?.Name ?? string.Empty,
            Price = ToDollars(subscription.ProductPriceInCents),
            State = subscription.State?.Value ?? "unknown",
            NextBillingAt = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
            AlreadyExisted = alreadyExisted
        };
    }

    private static decimal ToDollars(long? cents) =>
        cents is null ? 0m : decimal.Divide(cents.Value, 100m);

    private static (string FirstName, string LastName) SplitName(string email)
    {
        var local = email;
        var at = email.IndexOf('@');
        if (at > 0)
        {
            local = email[..at];
        }

        local = string.IsNullOrWhiteSpace(local) ? "Shopper" : local.Replace('.', ' ').Replace('_', ' ').Trim();
        if (string.IsNullOrWhiteSpace(local))
        {
            local = "Shopper";
        }

        return (char.ToUpperInvariant(local[0]) + local[1..], "eShopOnWeb");
    }

    private MaxioBillingException TranslateListProductsError(SdkException<ListProductsForProductFamilyError> ex)
    {
        if (ex.Error.TryGetString(out var message))
        {
            return new MaxioBillingException(
                string.IsNullOrWhiteSpace(message) ? "Product family was not found." : message,
                404,
                isCallerFault: true,
                ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return FromRaw("Unable to list subscription plans.", raw, ex);
        }

        return new MaxioBillingException("Unable to list subscription plans.", innerException: ex);
    }

    private static string FormatCustomerError(CreateCustomerError error)
    {
        if (!error.TryGetCustomerErrorResponse1(out var body) || body.Errors is null)
        {
            return "The billing customer could not be created.";
        }

        if (body.Errors.TryGetListOfString(out var list) && list.Count > 0)
        {
            return string.Join(" ", list);
        }

        if (body.Errors.TryGetCustomerError(out var customerError) && !string.IsNullOrWhiteSpace(customerError.Customer))
        {
            return customerError.Customer;
        }

        return "The billing customer could not be created.";
    }

    private static string FormatErrorList(ErrorListResponse1 list) =>
        list.Errors.Count == 0 ? "The subscription could not be created." : string.Join(" ", list.Errors);

    private static MaxioBillingException FromRaw(string fallback, RawError raw, Exception inner)
    {
        var status = (int)raw.StatusCode;
        var callerFault = status is >= 400 and < 500 && status is not 401 and not 403 and not 429;
        return new MaxioBillingException(fallback, status, callerFault, inner);
    }

    private static bool IsBoundaryException(Exception ex) =>
        ex is JsonException or HttpRequestException or TaskCanceledException or OperationCanceledException;

    private MaxioBillingException TranslateBoundary(Exception ex)
    {
        if (ex is OperationCanceledException && LastStatus.Value is null)
        {
            return new MaxioBillingException("The billing request was canceled.", innerException: ex);
        }

        if (ex is JsonException)
        {
            var status = LastStatus.Value is { } code ? (int)code : (int?)null;
            if (status is >= 400 and < 500 && status is not 401 and not 403 and not 429)
            {
                return new MaxioBillingException("The billing provider rejected the request.", status, isCallerFault: true, ex);
            }

            return new MaxioBillingException("The provider returned a response that could not be processed.", status, innerException: ex);
        }

        _logger.LogWarning(ex, "Maxio transport failure");
        return new MaxioBillingException("The billing provider is unreachable.", innerException: ex);
    }

    private static RequestOptions Observe() =>
        new()
        {
            Hooks =
            [
                SdkHook.OnResponse((response, _) => LastStatus.Value = response.StatusCode)
            ]
        };

    private static CancellationTokenSource LinkedDeadline(CancellationToken cancellationToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        return cts;
    }
}
