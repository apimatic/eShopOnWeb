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

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// <see cref="ISubscriptionService"/> backed by Maxio Advanced Billing. Every SDK call is
/// bounded by a whole-call cancellation budget and translated to <see cref="BillingException"/>
/// at this boundary, so callers see one failure type with a caller-safe message.
/// </summary>
public class MaxioSubscriptionService : ISubscriptionService
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    // A shopper with a subscription in any non-terminal state is already enrolled;
    // re-subscribing returns the existing one instead of creating a duplicate.
    private static readonly SubscriptionState[] TerminalStates =
    {
        SubscriptionState.Canceled,
        SubscriptionState.Expired,
        SubscriptionState.FailedToCreate
    };

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly IAppLogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        MaxioSettings settings,
        IAppLogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _settings = settings;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var products = await Bounded(
                ct => ListProductsAsync(ct),
                cancellationToken);

            return products
                .Select(p => p.Product)
                .Where(p => p is not null && p.ArchivedAt is null)
                .Select(p => new SubscriptionPlan
                {
                    Handle = p!.Handle ?? string.Empty,
                    Name = p.Name ?? string.Empty,
                    PriceInCents = p.PriceInCents ?? 0,
                    Interval = p.Interval ?? 0,
                    IntervalUnit = p.IntervalUnit?.Value ?? string.Empty
                })
                .ToList();
        }
        catch (Exception ex)
        {
            throw Translate(ex, cancellationToken);
        }
    }

    public async Task<SubscribeResult> SubscribeAsync(ShopperIdentity shopper, string productHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new BillingException(400, "A product handle is required.");
        }

        try
        {
            var customer = await Bounded(ct => FindOrCreateCustomerAsync(shopper, ct), cancellationToken);
            if (customer.Id is null)
            {
                throw new BillingException(502, "The billing provider returned a customer without an id.");
            }

            var existing = await Bounded(ct => ListCustomerSubscriptionsAsync(customer.Id.Value, ct), cancellationToken);
            var live = existing
                .Select(r => r.Subscription)
                .FirstOrDefault(s => s?.State is not null && !TerminalStates.Contains(s.State));
            if (live is not null)
            {
                return new SubscribeResult(Map(live), alreadySubscribed: true);
            }

            var created = await Bounded(ct => CreateSubscriptionAsync(customer.Id.Value, productHandle, ct), cancellationToken);
            if (created.Subscription is null)
            {
                throw new BillingException(502, "The billing provider returned an empty subscription.");
            }

            return new SubscribeResult(Map(created.Subscription), alreadySubscribed: false);
        }
        catch (Exception ex)
        {
            throw Translate(ex, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(string username, CancellationToken cancellationToken = default)
    {
        try
        {
            Customer? customer;
            try
            {
                var response = await Bounded(ct => _client.Customers.ReadCustomerByReference(username, ct), cancellationToken);
                customer = response.Customer;
            }
            catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                return Array.Empty<CustomerSubscription>();
            }

            if (customer?.Id is null)
            {
                return Array.Empty<CustomerSubscription>();
            }

            var subscriptions = await Bounded(ct => ListCustomerSubscriptionsAsync(customer.Id.Value, ct), cancellationToken);
            return subscriptions
                .Select(r => r.Subscription)
                .Where(s => s is not null)
                .Select(s => Map(s!))
                .ToList();
        }
        catch (Exception ex)
        {
            throw Translate(ex, cancellationToken);
        }
    }

    private async Task<IReadOnlyList<ProductResponse>> ListProductsAsync(CancellationToken ct)
    {
        try
        {
            return await ListProductsPageAsync("handle:" + _settings.ProductFamilyHandle, ct);
        }
        catch (SdkException<ListProductsForProductFamilyError> ex) when (ex.Error.TryGetString(out _))
        {
            // 404: the server may not honor the "handle:" prefix — resolve the numeric
            // family id by matching the handle client-side, then retry with it.
            var familyId = await ResolveFamilyIdAsync(ct);
            return await ListProductsPageAsync(familyId, ct);
        }
    }

    private Task<IReadOnlyList<ProductResponse>> ListProductsPageAsync(string productFamilyId, CancellationToken ct) =>
        _client.ProductFamilies.ListProductsForProductFamily(
            productFamilyId: productFamilyId,
            dateField: null,
            filter: null,
            startDate: null,
            endDate: null,
            startDatetime: null,
            endDatetime: null,
            includeArchived: null,
            include: null,
            page: 1,
            perPage: 200,
            ct: ct);

    private async Task<string> ResolveFamilyIdAsync(CancellationToken ct)
    {
        var families = await _client.ProductFamilies.ListProductFamilies(null, null, null, null, null, ct: ct);
        var match = families
            .Select(f => f.ProductFamily)
            .FirstOrDefault(f => string.Equals(f?.Handle, _settings.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase));
        if (match?.Id is null)
        {
            throw new BillingException(404, $"Product family '{_settings.ProductFamilyHandle}' was not found at the billing provider.");
        }

        return match.Id.Value.ToString();
    }

    private async Task<Customer> FindOrCreateCustomerAsync(ShopperIdentity shopper, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(shopper.Username, ct);
            if (response.Customer is not null)
            {
                return response.Customer;
            }
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            // Customer does not exist yet — fall through to create.
        }

        try
        {
            var created = await _client.Customers.CreateCustomer(
                new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = shopper.FirstName,
                        LastName = shopper.LastName,
                        Email = shopper.Email,
                        Reference = shopper.Username
                    }
                },
                ct: ct);
            if (created.Customer is null)
            {
                throw new BillingException(502, "The billing provider returned an empty customer.");
            }

            return created.Customer;
        }
        catch (SdkException<CreateCustomerError> ex) when (ex.Error.TryGetCustomerErrorResponse1(out _))
        {
            // 422 — most likely a concurrent first-time request won the create race
            // (reference is unique per customer): re-read and continue with the winner.
            var response = await _client.Customers.ReadCustomerByReference(shopper.Username, ct);
            if (response.Customer is not null)
            {
                return response.Customer;
            }

            throw new BillingException(422, "The billing provider rejected the customer record.", ex);
        }
    }

    private Task<IReadOnlyList<SubscriptionResponse>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct) =>
        _client.Customers.ListCustomerSubscriptions(customerId, ct: ct);

    private async Task<SubscriptionResponse> CreateSubscriptionAsync(int customerId, string productHandle, CancellationToken ct)
    {
        try
        {
            return await _client.Subscriptions.CreateSubscription(
                new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = productHandle,
                        CustomerId = customerId
                    }
                },
                ct: ct);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                var detail = string.Join("; ", errorList.Errors);
                _logger.LogWarning("Maxio rejected subscription creation: {Detail}", detail);
                throw new BillingException(422, $"The billing provider rejected the subscription: {detail}", ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new BillingException((int)raw.StatusCode, "The billing provider rejected the subscription.", ex);
            }

            throw new BillingException(502, "The billing provider returned an unrecognized error.", ex);
        }
    }

    private static CustomerSubscription Map(Subscription subscription) =>
        new CustomerSubscription
        {
            Id = subscription.Id ?? 0,
            State = subscription.State?.Value ?? string.Empty,
            ProductName = subscription.Product?.Name ?? string.Empty,
            ProductHandle = subscription.Product?.Handle ?? string.Empty,
            PriceInCents = subscription.ProductPriceInCents ?? subscription.CurrentBillingAmountInCents ?? 0,
            Currency = subscription.Currency ?? string.Empty,
            NextBillingDate = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt
        };

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private Exception Translate(Exception ex, CancellationToken requestToken)
    {
        switch (ex)
        {
            case BillingException:
                return ex;
            case SdkException<ListProductsForProductFamilyError> typed when typed.Error.TryGetRawError(out var raw):
                return new BillingException((int)raw.StatusCode, "The billing provider rejected the plan listing.", ex);
            case SdkException<RawError> rawError:
                return new BillingException((int)rawError.Error.StatusCode, "The billing provider rejected the request.", ex);
            case JsonException:
                return new BillingException(502, "The billing provider returned a response that could not be processed.", ex);
            case HttpRequestException:
                return new BillingException(503, "The billing provider is unreachable.", ex);
            case TaskCanceledException when !requestToken.IsCancellationRequested:
                return new BillingException(504, "The billing provider did not respond in time.", ex);
            default:
                return ex;
        }
    }
}
