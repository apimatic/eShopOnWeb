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
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

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

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var familyHandle = _options.ProductFamilyHandle;
        var plans = new List<SubscriptionPlan>();
        var page = 1;
        const int perPage = 200;

        try
        {
            while (true)
            {
                var batch = await Bounded(
                    ct => _client.ProductFamilies.ListProductsForProductFamily(
                        productFamilyId: "handle:" + familyHandle,
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

                foreach (var wrapper in batch)
                {
                    var product = wrapper.Product;
                    if (string.IsNullOrWhiteSpace(product.Handle))
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
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            throw TranslateListProductsError(ex);
        }
        catch (Exception ex) when (IsBoundaryException(ex))
        {
            throw TranslateBoundary(ex, isWrite: false);
        }
    }

    public async Task<ShopperSubscription> SubscribeAsync(
        string userId,
        string email,
        string firstName,
        string lastName,
        string productHandle,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new MaxioBillingException("A product handle is required.", HttpStatusCode.BadRequest);
        }

        var customer = await EnsureCustomerAsync(userId, email, firstName, lastName, cancellationToken);
        if (customer.Id is not int customerId)
        {
            throw new MaxioBillingException(
                "The billing provider returned a customer without an id.",
                HttpStatusCode.BadGateway);
        }

        var existing = await FindLiveSubscriptionAsync(customerId, productHandle, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            SubscriptionResponse created;
            using (WriteOnceScope.Arm())
            {
                created = await Bounded(
                    ct => _client.Subscriptions.CreateSubscription(
                        body: new CreateSubscriptionRequest
                        {
                            Subscription = new CreateSubscription
                            {
                                ProductHandle = productHandle,
                                CustomerId = customerId,
                                PaymentCollectionMethod = CollectionMethod.Remittance
                            }
                        },
                        ct: ct),
                    cancellationToken);
            }

            var subscription = created.Subscription
                ?? throw new MaxioBillingException(
                    "The billing provider returned an empty subscription.",
                    HttpStatusCode.BadGateway);
            return MapSubscription(subscription);
        }
        catch (DuplicateWritePreventedException)
        {
            _logger.LogWarning("CreateSubscription send was blocked; reconciling provider state for {UserId} {Handle}", userId, productHandle);
            return await ReconcileCreatedSubscriptionAsync(customerId, productHandle, cancellationToken);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            var live = await FindLiveSubscriptionAsync(customerId, productHandle, cancellationToken);
            if (live is not null)
            {
                return live;
            }

            throw TranslateCreateSubscriptionError(ex);
        }
        catch (Exception ex) when (IsBoundaryException(ex))
        {
            var live = await TryFindLiveSubscriptionAsync(customerId, productHandle, cancellationToken);
            if (live is not null)
            {
                return live;
            }

            throw TranslateBoundary(ex, isWrite: true);
        }
    }

    public async Task<IReadOnlyList<ShopperSubscription>> ListMySubscriptionsAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        Customer? customer;
        try
        {
            var response = await Bounded(
                ct => _client.Customers.ReadCustomerByReference(reference: userId, ct: ct),
                cancellationToken);
            customer = response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return Array.Empty<ShopperSubscription>();
        }
        catch (Exception ex) when (IsBoundaryException(ex))
        {
            throw TranslateBoundary(ex, isWrite: false);
        }

        if (customer.Id is not int customerId)
        {
            return Array.Empty<ShopperSubscription>();
        }

        try
        {
            var wrappers = await Bounded(
                ct => _client.Customers.ListCustomerSubscriptions(customerId: customerId, ct: ct),
                cancellationToken);

            return wrappers
                .Select(w => w.Subscription)
                .Where(s => s is not null)
                .Select(s => MapSubscription(s!))
                .ToList();
        }
        catch (Exception ex) when (IsBoundaryException(ex))
        {
            throw TranslateBoundary(ex, isWrite: false);
        }
    }

    private async Task<Customer> EnsureCustomerAsync(
        string userId,
        string email,
        string firstName,
        string lastName,
        CancellationToken cancellationToken)
    {
        try
        {
            var existing = await Bounded(
                ct => _client.Customers.ReadCustomerByReference(reference: userId, ct: ct),
                cancellationToken);
            return existing.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            // create below
        }
        catch (Exception ex) when (IsBoundaryException(ex))
        {
            throw TranslateBoundary(ex, isWrite: false);
        }

        try
        {
            CustomerResponse created;
            using (WriteOnceScope.Arm())
            {
                created = await Bounded(
                    ct => _client.Customers.CreateCustomer(
                        body: new CreateCustomerRequest
                        {
                            Customer = new CreateCustomer
                            {
                                FirstName = firstName,
                                LastName = lastName,
                                Email = email,
                                Reference = userId
                            }
                        },
                        ct: ct),
                    cancellationToken);
            }

            return created.Customer;
        }
        catch (DuplicateWritePreventedException)
        {
            _logger.LogWarning("CreateCustomer send was blocked; reconciling by reference {UserId}", userId);
            return await ReadCustomerOrThrowAsync(userId, cancellationToken);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            var raced = await TryReadCustomerAsync(userId, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw TranslateCreateCustomerError(ex);
        }
        catch (Exception ex) when (IsBoundaryException(ex))
        {
            var raced = await TryReadCustomerAsync(userId, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw TranslateBoundary(ex, isWrite: true);
        }
    }

    private async Task<ShopperSubscription?> FindLiveSubscriptionAsync(
        int customerId,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var wrappers = await Bounded(
            ct => _client.Customers.ListCustomerSubscriptions(customerId: customerId, ct: ct),
            cancellationToken);

        foreach (var wrapper in wrappers)
        {
            var subscription = wrapper.Subscription;
            if (subscription is null)
            {
                continue;
            }

            if (!string.Equals(subscription.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (IsLiveEnrollment(subscription.State))
            {
                return MapSubscription(subscription);
            }
        }

        return null;
    }

    private async Task<ShopperSubscription?> TryFindLiveSubscriptionAsync(
        int customerId,
        string productHandle,
        CancellationToken cancellationToken)
    {
        try
        {
            return await FindLiveSubscriptionAsync(customerId, productHandle, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to reconcile subscriptions for customer {CustomerId}: {Message}", customerId, ex.GetType().Name);
            return null;
        }
    }

    private async Task<ShopperSubscription> ReconcileCreatedSubscriptionAsync(
        int customerId,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var live = await TryFindLiveSubscriptionAsync(customerId, productHandle, cancellationToken);
        if (live is not null)
        {
            return live;
        }

        throw new MaxioBillingException(
            "The subscription request may have reached the billing provider. Refresh your subscriptions and retry only if it is missing.",
            HttpStatusCode.ServiceUnavailable);
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
        catch (Exception)
        {
            return null;
        }
    }

    private async Task<Customer> ReadCustomerOrThrowAsync(string userId, CancellationToken cancellationToken)
    {
        var customer = await TryReadCustomerAsync(userId, cancellationToken);
        if (customer is not null)
        {
            return customer;
        }

        throw new MaxioBillingException(
            "The customer request may have reached the billing provider. Retry the subscription.",
            HttpStatusCode.ServiceUnavailable);
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey) ||
            string.IsNullOrWhiteSpace(_options.Subdomain) ||
            string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw new MaxioBillingException(
                "Maxio billing is not configured. Set Maxio:ApiKey, Maxio:Subdomain, and Maxio:ProductFamilyHandle.",
                HttpStatusCode.ServiceUnavailable);
        }
    }

    private static bool IsLiveEnrollment(SubscriptionState? state)
    {
        if (state is null)
        {
            return true;
        }

        if (state == SubscriptionState.Canceled ||
            state == SubscriptionState.Expired ||
            state == SubscriptionState.TrialEnded ||
            state == SubscriptionState.FailedToCreate)
        {
            return false;
        }

        return true;
    }

    private static SubscriptionPlan MapPlan(Product product)
    {
        return new SubscriptionPlan
        {
            Handle = product.Handle ?? string.Empty,
            Name = product.Name ?? string.Empty,
            Description = product.Description,
            Price = ToMoney(product.PriceInCents),
            Interval = product.Interval ?? 0,
            IntervalUnit = product.IntervalUnit?.Value ?? string.Empty,
            RequireCreditCard = product.RequireCreditCard ?? false
        };
    }

    private static ShopperSubscription MapSubscription(Subscription subscription)
    {
        return new ShopperSubscription
        {
            Id = subscription.Id ?? 0,
            ProductHandle = subscription.Product?.Handle ?? string.Empty,
            ProductName = subscription.Product?.Name ?? string.Empty,
            Price = ToMoney(subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents),
            State = subscription.State?.Value ?? string.Empty,
            NextBillingDate = subscription.NextAssessmentAt
        };
    }

    private static decimal ToMoney(long? cents) => (cents ?? 0) / 100m;

    private static bool IsBoundaryException(Exception ex) =>
        ex is SdkException<RawError>
            or HttpRequestException
            or TaskCanceledException
            or JsonException
            or DuplicateWritePreventedException;

    private MaxioBillingException TranslateListProductsError(SdkException<ListProductsForProductFamilyError> ex)
    {
        if (ex.Error.TryGetString(out var message))
        {
            return new MaxioBillingException(
                string.IsNullOrWhiteSpace(message) ? "Subscription plans were not found." : message,
                HttpStatusCode.NotFound,
                ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return FromRaw(raw, ex);
        }

        return new MaxioBillingException("The billing provider rejected the plans request.", HttpStatusCode.BadGateway, ex);
    }

    private MaxioBillingException TranslateCreateCustomerError(SdkException<CreateCustomerError> ex)
    {
        if (ex.Error.TryGetCustomerErrorResponse1(out var body))
        {
            var detail = ExtractCustomerError(body);
            return new MaxioBillingException(
                string.IsNullOrWhiteSpace(detail) ? "The billing provider rejected the customer." : detail,
                HttpStatusCode.UnprocessableEntity,
                ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return FromRaw(raw, ex);
        }

        return new MaxioBillingException("The billing provider rejected the customer.", HttpStatusCode.BadGateway, ex);
    }

    private MaxioBillingException TranslateCreateSubscriptionError(SdkException<CreateSubscriptionError> ex)
    {
        if (ex.Error.TryGetErrorListResponse1(out var list))
        {
            var detail = list.Errors is { Count: > 0 } ? string.Join(" ", list.Errors) : null;
            return new MaxioBillingException(
                string.IsNullOrWhiteSpace(detail) ? "The billing provider rejected the subscription." : detail,
                HttpStatusCode.UnprocessableEntity,
                ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return FromRaw(raw, ex);
        }

        return new MaxioBillingException("The billing provider rejected the subscription.", HttpStatusCode.BadGateway, ex);
    }

    private static string? ExtractCustomerError(CustomerErrorResponse1 body)
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

    private MaxioBillingException TranslateBoundary(Exception ex, bool isWrite)
    {
        switch (ex)
        {
            case SdkException<RawError> rawEx:
                return FromRaw(rawEx.Error, rawEx);
            case JsonException:
                return new MaxioBillingException(
                    isWrite
                        ? "The billing provider rejected the request."
                        : "The billing provider returned a response that could not be processed.",
                    isWrite ? HttpStatusCode.UnprocessableEntity : HttpStatusCode.BadGateway,
                    ex);
            case TaskCanceledException:
            case HttpRequestException:
                return new MaxioBillingException(
                    isWrite
                        ? "The billing provider could not be reached. Refresh your subscriptions and retry only if it is missing."
                        : "The billing provider could not be reached.",
                    HttpStatusCode.BadGateway,
                    ex);
            default:
                return new MaxioBillingException("The billing provider request failed.", HttpStatusCode.BadGateway, ex);
        }
    }

    private static MaxioBillingException FromRaw(RawError raw, Exception inner)
    {
        var status = raw.StatusCode;
        var mapped = (int)status >= 400 && (int)status < 500 ? status : HttpStatusCode.BadGateway;
        var body = SafeRead(raw);
        var message = string.IsNullOrWhiteSpace(body)
            ? "The billing provider returned an error."
            : "The billing provider returned an error.";
        // Keep the public message caller-safe; log-worthy detail stays on the inner exception.
        _ = message;
        return new MaxioBillingException(
            (int)status is >= 400 and < 500
                ? "The billing provider rejected the request."
                : "The billing provider is unavailable.",
            mapped,
            inner);
    }

    private static string SafeRead(RawError raw)
    {
        try
        {
            return raw.ReadAsString();
        }
        catch
        {
            return string.Empty;
        }
    }
}
