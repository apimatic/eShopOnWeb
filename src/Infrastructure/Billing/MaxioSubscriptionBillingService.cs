using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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

public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly HashSet<string> ClosedStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled",
        "expired",
        "failed_to_create",
        "trial_ended"
    };

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
        using var cts = Bound(cancellationToken);

        try
        {
            var familyId = await ResolveProductFamilyIdAsync(_options.ProductFamilyHandle, cts.Token);
            var products = await _client.ProductFamilies.ListProductsForProductFamily(
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
                perPage: 20,
                ct: cts.Token);

            return products
                .Select(item => item.Product)
                .Where(product => product is not null && !string.IsNullOrWhiteSpace(product.Handle))
                .Select(product => MapPlan(product!))
                .ToList();
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            throw MapListProductsError(ex);
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            throw Unavailable("listing subscription plans", ex);
        }
        catch (JsonException ex)
        {
            throw MapJson(ex, "listing subscription plans");
        }
    }

    public async Task<ShopSubscription> SubscribeAsync(
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
            throw new BillingException(400, "A product handle is required.");
        }

        using var cts = Bound(cancellationToken);
        var customer = await EnsureCustomerAsync(userId, email, firstName, lastName, cts.Token);
        var customerId = customer.Id
            ?? throw new BillingException(502, "Billing provider returned a customer without an id.");

        var existing = await FindOpenSubscriptionAsync(customerId, productHandle, cts.Token);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            var created = await _client.Subscriptions.CreateSubscription(
                body: new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = productHandle,
                        CustomerId = customerId,
                        PaymentCollectionMethod = CollectionMethod.Invoice
                    }
                },
                ct: cts.Token);

            if (created.Subscription is null)
            {
                throw new BillingException(502, "Billing provider returned an empty subscription.");
            }

            return MapSubscription(created.Subscription);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            var raced = await FindOpenSubscriptionAsync(customerId, productHandle, cts.Token);
            if (raced is not null)
            {
                return raced;
            }

            throw MapCreateSubscriptionError(ex);
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            var raced = await FindOpenSubscriptionAsync(customerId, productHandle, CancellationToken.None);
            if (raced is not null)
            {
                return raced;
            }

            throw Unavailable("creating a subscription", ex);
        }
        catch (JsonException ex)
        {
            var raced = await FindOpenSubscriptionAsync(customerId, productHandle, CancellationToken.None);
            if (raced is not null)
            {
                return raced;
            }

            throw MapJson(ex, "creating a subscription");
        }
    }

    public async Task<IReadOnlyList<ShopSubscription>> ListMySubscriptionsAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        using var cts = Bound(cancellationToken);

        var customer = await TryReadCustomerByReferenceAsync(userId, cts.Token);
        if (customer?.Id is null)
        {
            return Array.Empty<ShopSubscription>();
        }

        return await ListCustomerSubscriptionsAsync(customer.Id.Value, cts.Token);
    }

    private async Task<Customer> EnsureCustomerAsync(
        string userId,
        string email,
        string firstName,
        string lastName,
        CancellationToken cancellationToken)
    {
        var existing = await TryReadCustomerByReferenceAsync(userId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

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
                        Reference = userId
                    }
                },
                ct: cancellationToken);

            return created.Customer
                ?? throw new BillingException(502, "Billing provider returned an empty customer.");
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            var raced = await TryReadCustomerByReferenceAsync(userId, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw MapCreateCustomerError(ex);
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            var raced = await TryReadCustomerByReferenceAsync(userId, CancellationToken.None);
            if (raced is not null)
            {
                return raced;
            }

            throw Unavailable("creating a billing customer", ex);
        }
        catch (JsonException ex)
        {
            var raced = await TryReadCustomerByReferenceAsync(userId, CancellationToken.None);
            if (raced is not null)
            {
                return raced;
            }

            throw MapJson(ex, "creating a billing customer");
        }
    }

    private async Task<Customer?> TryReadCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(
                reference: reference,
                ct: cancellationToken);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when ((int)ex.Error.StatusCode == 404)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRaw("looking up the billing customer", ex.Error);
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            throw Unavailable("looking up the billing customer", ex);
        }
        catch (JsonException ex)
        {
            throw MapJson(ex, "looking up the billing customer");
        }
    }

    private async Task<ShopSubscription?> FindOpenSubscriptionAsync(
        int customerId,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var subscriptions = await ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(item =>
            string.Equals(item.ProductHandle, productHandle, StringComparison.OrdinalIgnoreCase)
            && !ClosedStates.Contains(item.State));
    }

    private async Task<IReadOnlyList<ShopSubscription>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken)
    {
        try
        {
            var responses = await _client.Customers.ListCustomerSubscriptions(
                customerId: customerId,
                ct: cancellationToken);

            return responses
                .Select(item => item.Subscription)
                .Where(subscription => subscription is not null)
                .Select(subscription => MapSubscription(subscription!))
                .ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRaw("listing subscriptions", ex.Error);
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            throw Unavailable("listing subscriptions", ex);
        }
        catch (JsonException ex)
        {
            throw MapJson(ex, "listing subscriptions");
        }
    }

    private async Task<string> ResolveProductFamilyIdAsync(string familyHandle, CancellationToken cancellationToken)
    {
        try
        {
            var families = await _client.ProductFamilies.ListProductFamilies(
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                ct: cancellationToken);

            var family = families
                .Select(item => item.ProductFamily)
                .FirstOrDefault(item =>
                    item is not null
                    && string.Equals(item.Handle, familyHandle, StringComparison.OrdinalIgnoreCase));

            if (family?.Id is int id)
            {
                return id.ToString(CultureInfo.InvariantCulture);
            }

            throw new BillingException(404, "The configured subscription catalog was not found.");
        }
        catch (BillingException)
        {
            throw;
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRaw("loading the subscription catalog", ex.Error);
        }
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey)
            || string.IsNullOrWhiteSpace(_options.Subdomain)
            || string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw new BillingException(503, "Subscription billing is not configured.");
        }
    }

    private static CancellationTokenSource Bound(CancellationToken cancellationToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(30));
        return cts;
    }

    private static SubscriptionPlan MapPlan(Product product)
    {
        return new SubscriptionPlan
        {
            Handle = product.Handle ?? string.Empty,
            Name = product.Name ?? product.Handle ?? "Plan",
            Description = product.Description,
            PriceInCents = product.PriceInCents,
            Price = product.PriceInCents is long cents ? cents / 100m : 0m,
            Interval = product.Interval,
            IntervalUnit = product.IntervalUnit is null ? null : (string)product.IntervalUnit
        };
    }

    private static ShopSubscription MapSubscription(Subscription subscription)
    {
        var state = subscription.State is null ? string.Empty : (string)subscription.State;
        return new ShopSubscription
        {
            Id = subscription.Id ?? 0,
            State = state,
            ProductHandle = subscription.Product?.Handle,
            ProductName = subscription.Product?.Name,
            Price = subscription.Product?.PriceInCents is long cents ? cents / 100m : null,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            NextAssessmentAt = subscription.NextAssessmentAt
        };
    }

    private BillingException MapCreateCustomerError(SdkException<CreateCustomerError> ex)
    {
        if (ex.Error.TryGetCustomerErrorResponse1(out _))
        {
            _logger.LogWarning("Maxio create customer was rejected.");
            return new BillingException(422, "Unable to create a billing customer for this account.");
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return MapRaw("creating a billing customer", raw);
        }

        return new BillingException(502, "Billing provider returned an unexpected error.");
    }

    private BillingException MapCreateSubscriptionError(SdkException<CreateSubscriptionError> ex)
    {
        if (ex.Error.TryGetErrorListResponse1(out var list))
        {
            var details = list.Errors is { Count: > 0 } ? string.Join("; ", list.Errors) : "no details";
            _logger.LogWarning("Maxio create subscription was rejected: {Details}.", details);
            return new BillingException(422, "Unable to start the subscription with the selected plan.");
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return MapRaw("creating a subscription", raw);
        }

        return new BillingException(502, "Billing provider returned an unexpected error.");
    }

    private BillingException MapListProductsError(SdkException<ListProductsForProductFamilyError> ex)
    {
        if (ex.Error.TryGetString(out _))
        {
            return new BillingException(404, "The configured subscription catalog was not found.");
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return MapRaw("listing subscription plans", raw);
        }

        return new BillingException(502, "Billing provider returned an unexpected error.");
    }

    private BillingException MapRaw(string operation, RawError raw)
    {
        var status = (int)raw.StatusCode;
        _logger.LogWarning("Maxio {Operation} failed with HTTP {Status}.", operation, status);
        if (status == 404)
        {
            return new BillingException(404, "The requested billing record was not found.");
        }

        if (status >= 400 && status < 500)
        {
            return new BillingException(400, "The billing request was rejected.");
        }

        return new BillingException(502, "Billing provider request failed.");
    }

    private BillingException MapJson(JsonException ex, string operation)
    {
        var status = LastStatusHandler.LastStatusCode;
        _logger.LogWarning("Maxio {Operation} returned an unreadable body (HTTP {Status}).", operation, status?.ToString() ?? "unknown");
        if (status is >= 400 and < 500)
        {
            return new BillingException(400, "The billing request was rejected.");
        }

        return new BillingException(502, "The billing provider returned a response that could not be processed.");
    }

    private BillingException Unavailable(string operation, Exception ex)
    {
        _logger.LogWarning("Maxio {Operation} is unreachable: {ExceptionType}.", operation, ex.GetType().Name);
        return new BillingException(503, "The billing provider is currently unavailable.");
    }

    private static bool IsTransport(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException;
}
