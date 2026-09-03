using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Maxio;
using Maxio.Core.ErrorResponse;
using Maxio.Core.Exceptions;
using Maxio.Errors;
using Maxio.Models;
using Maxio.Models.Enums;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    private readonly MaxioClient _client;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        MaxioClient client,
        IOptions<MaxioOptions> options,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public Task<IReadOnlyList<CatalogPlan>> ListPlansAsync(CancellationToken cancellationToken) =>
        Bounded(async ct =>
        {
            var familyId = "handle:" + _options.ProductFamilyHandle;
            try
            {
                var responses = await _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: familyId,
                    dateField: null,
                    filter: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    includeArchived: false,
                    include: null,
                    page: 1,
                    perPage: 200,
                    ct: ct);

                var plans = responses
                    .Select(r => r.Product)
                    .Where(p => p.ArchivedAt is null && !string.IsNullOrWhiteSpace(p.Handle))
                    .Select(MapPlan)
                    .ToList();

                _logger.LogInformation("Listed {PlanCount} Maxio subscription plans for family {FamilyHandle}.",
                    plans.Count, _options.ProductFamilyHandle);
                return (IReadOnlyList<CatalogPlan>)plans;
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetString(out var notFound))
                {
                    throw new MaxioBillingException(
                        string.IsNullOrWhiteSpace(notFound)
                            ? "Subscription catalog was not found."
                            : notFound,
                        StatusCodes.NotFound,
                        ex);
                }

                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw MapRaw(raw, "Unable to list subscription plans.", ex);
                }

                throw new MaxioBillingException("Unable to list subscription plans.", StatusCodes.BadGateway, ex);
            }
        }, cancellationToken);

    public Task<ShopperSubscription> SubscribeAsync(
        string userId,
        string email,
        string productHandle,
        CancellationToken cancellationToken) =>
        Bounded(async ct =>
        {
            var subscriptionReference = BuildSubscriptionReference(userId, productHandle);
            var existing = await TryFindSubscriptionAsync(subscriptionReference, ct);
            if (existing is not null)
            {
                _logger.LogInformation(
                    "Returning existing Maxio subscription {SubscriptionId} for user {UserId} product {ProductHandle}.",
                    existing.Id, userId, productHandle);
                return existing;
            }

            var customer = await EnsureCustomerAsync(userId, email, ct);
            if (customer.Id is null)
            {
                throw new MaxioBillingException(
                    "The billing provider returned a customer without an id.",
                    StatusCodes.BadGateway);
            }

            try
            {
                var created = await _client.Subscriptions.CreateSubscription(
                    body: new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            ProductHandle = productHandle,
                            CustomerId = customer.Id,
                            Reference = subscriptionReference,
                            PaymentCollectionMethod = CollectionMethod.Remittance
                        }
                    },
                    ct: ct);

                var mapped = MapSubscription(created)
                    ?? throw new MaxioBillingException(
                        "The billing provider returned a subscription without a body.",
                        StatusCodes.BadGateway);

                _logger.LogInformation(
                    "Created Maxio subscription {SubscriptionId} state {State} for user {UserId} product {ProductHandle}.",
                    mapped.Id, mapped.State, userId, productHandle);
                return mapped;
            }
            catch (SdkException<CreateSubscriptionError> ex)
            {
                if (ex.Error.TryGetErrorListResponse1(out var list))
                {
                    var raced = await TryFindSubscriptionAsync(subscriptionReference, ct);
                    if (raced is not null)
                    {
                        _logger.LogWarning(
                            "CreateSubscription 422 for user {UserId} product {ProductHandle}; reconciled to existing subscription {SubscriptionId}.",
                            userId, productHandle, raced.Id);
                        return raced;
                    }

                    throw new MaxioBillingException(FormatErrorList(list.Errors), StatusCodes.Unprocessable, ex);
                }

                if (ex.Error.TryGetRawError(out var raw))
                {
                    var raced = await TryFindSubscriptionAsync(subscriptionReference, ct);
                    if (raced is not null)
                    {
                        return raced;
                    }

                    throw MapRaw(raw, "Unable to create the subscription.", ex);
                }

                throw new MaxioBillingException("Unable to create the subscription.", StatusCodes.BadGateway, ex);
            }
        }, cancellationToken);

    public Task<IReadOnlyList<ShopperSubscription>> ListSubscriptionsForUserAsync(
        string userId,
        CancellationToken cancellationToken) =>
        Bounded(async ct =>
        {
            var customer = await TryReadCustomerAsync(userId, ct);
            if (customer?.Id is null)
            {
                return Array.Empty<ShopperSubscription>();
            }

            try
            {
                var responses = await _client.Customers.ListCustomerSubscriptions(
                    customerId: customer.Id.Value,
                    ct: ct);

                var subscriptions = responses
                    .Select(MapSubscription)
                    .Where(s => s is not null)
                    .Select(s => s!)
                    .ToList();

                _logger.LogInformation(
                    "Listed {SubscriptionCount} Maxio subscriptions for user {UserId} (customer {CustomerId}).",
                    subscriptions.Count, userId, customer.Id);
                return (IReadOnlyList<ShopperSubscription>)subscriptions;
            }
            catch (SdkException<RawError> ex)
            {
                throw MapRaw(ex.Error, "Unable to list subscriptions.", ex);
            }
        }, cancellationToken);

    public static string BuildSubscriptionReference(string userId, string productHandle) =>
        $"eshop:{userId}:{productHandle}";

    private async Task<Customer> EnsureCustomerAsync(string userId, string email, CancellationToken ct)
    {
        var existing = await TryReadCustomerAsync(userId, ct);
        if (existing is not null)
        {
            return existing;
        }

        var names = SplitDisplayName(email);
        try
        {
            var created = await _client.Customers.CreateCustomer(
                body: new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = names.FirstName,
                        LastName = names.LastName,
                        Email = email,
                        Reference = userId
                    }
                },
                ct: ct);

            _logger.LogInformation("Created Maxio customer {CustomerId} for user {UserId}.",
                created.Customer.Id, userId);
            return created.Customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var body))
            {
                var raced = await TryReadCustomerAsync(userId, ct);
                if (raced is not null)
                {
                    _logger.LogWarning(
                        "CreateCustomer 422 for user {UserId}; reconciled to existing customer {CustomerId}.",
                        userId, raced.Id);
                    return raced;
                }

                throw new MaxioBillingException(FormatCustomerErrors(body), StatusCodes.Unprocessable, ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                var raced = await TryReadCustomerAsync(userId, ct);
                if (raced is not null)
                {
                    return raced;
                }

                throw MapRaw(raw, "Unable to create the billing customer.", ex);
            }

            throw new MaxioBillingException("Unable to create the billing customer.", StatusCodes.BadGateway, ex);
        }
    }

    private async Task<Customer?> TryReadCustomerAsync(string userId, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference: userId, ct: ct);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRaw(ex.Error, "Unable to look up the billing customer.", ex);
        }
    }

    private async Task<ShopperSubscription?> TryFindSubscriptionAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Subscriptions.FindSubscription(reference: reference, ct: ct);
            return MapSubscription(response);
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                return null;
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw MapRaw(raw, "Unable to look up the subscription.", ex);
            }

            throw new MaxioBillingException("Unable to look up the subscription.", StatusCodes.BadGateway, ex);
        }
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        MaxioCallContext.LastHttpStatus = null;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);

        try
        {
            return await call(cts.Token);
        }
        catch (MaxioBillingException)
        {
            throw;
        }
        catch (JsonException ex)
        {
            throw MapJsonException(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new MaxioBillingException("Billing provider unavailable.", StatusCodes.BadGateway, ex);
        }
    }

    private MaxioBillingException MapJsonException(JsonException ex)
    {
        var status = MaxioCallContext.LastHttpStatus;
        if (status is >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError)
        {
            _logger.LogWarning(ex,
                "Billing provider rejected the request (HTTP {Status}) but the error body could not be parsed.",
                (int)status.Value);
            return new MaxioBillingException("The billing provider rejected the request.", (int)status.Value, ex);
        }

        _logger.LogWarning(ex, "Billing provider returned a response that could not be processed.");
        return new MaxioBillingException(
            "The billing provider returned a response that could not be processed.",
            StatusCodes.BadGateway,
            ex);
    }

    private MaxioBillingException MapRaw(RawError raw, string fallback, Exception inner)
    {
        var status = (int)raw.StatusCode;
        var body = Truncate(raw.ReadAsString(), 500);
        _logger.LogWarning("Maxio HTTP {Status}: {Body}", status, body);

        return status switch
        {
            401 or 403 => new MaxioBillingException("Billing provider unavailable.", StatusCodes.BadGateway, inner),
            429 => new MaxioBillingException("Temporarily unavailable.", StatusCodes.ServiceUnavailable, inner),
            >= 400 and < 500 => new MaxioBillingException(
                string.IsNullOrWhiteSpace(body) ? fallback : body,
                status,
                inner),
            _ => new MaxioBillingException(fallback, StatusCodes.BadGateway, inner)
        };
    }

    private static CatalogPlan MapPlan(Product product)
    {
        var cents = product.PriceInCents ?? 0;
        return new CatalogPlan(
            Handle: product.Handle!,
            Name: product.Name ?? product.Handle!,
            Description: product.Description,
            PriceInCents: cents,
            Price: cents / 100m,
            Interval: product.Interval,
            IntervalUnit: product.IntervalUnit?.Value);
    }

    private static ShopperSubscription? MapSubscription(SubscriptionResponse response)
    {
        var subscription = response.Subscription;
        if (subscription?.Id is null)
        {
            return null;
        }

        var cents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? 0;
        var nextBilling = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt;
        var state = subscription.State?.Value ?? "unknown";

        return new ShopperSubscription(
            Id: subscription.Id.Value,
            ProductHandle: subscription.Product?.Handle,
            ProductName: subscription.Product?.Name,
            State: state,
            PriceInCents: cents,
            Price: cents / 100m,
            NextBillingAt: nextBilling,
            Reference: subscription.Reference);
    }

    public static (string FirstName, string LastName) SplitDisplayName(string email)
    {
        var at = email.IndexOf('@');
        var local = at > 0 ? email[..at] : email;
        local = string.IsNullOrWhiteSpace(local) ? "Shopper" : local.Trim();
        return (local, "eShopOnWeb");
    }

    private static string FormatErrorList(IReadOnlyList<string> errors) =>
        errors.Count == 0 ? "The billing provider rejected the request." : string.Join(" ", errors);

    private static string FormatCustomerErrors(CustomerErrorResponse1 body)
    {
        if (body.Errors is null)
        {
            return "The billing provider rejected the customer.";
        }

        if (body.Errors.TryGetListOfString(out var list))
        {
            return FormatErrorList(list);
        }

        if (body.Errors.TryGetCustomerError(out var customerError)
            && !string.IsNullOrWhiteSpace(customerError.Customer))
        {
            return customerError.Customer;
        }

        return "The billing provider rejected the customer.";
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];

    private static class StatusCodes
    {
        public const int BadGateway = 502;
        public const int ServiceUnavailable = 503;
        public const int NotFound = 404;
        public const int Unprocessable = 422;
    }
}
