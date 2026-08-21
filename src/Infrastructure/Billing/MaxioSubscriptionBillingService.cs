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
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private const int ProductPageSize = 200;
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

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var familyHandle = _options.ProductFamilyHandle;
        var plans = new List<SubscriptionPlan>();

        try
        {
            var page = 1;
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
                        perPage: ProductPageSize,
                        ct: ct),
                    cancellationToken);

                if (batch is null || batch.Count == 0)
                {
                    break;
                }

                foreach (var row in batch)
                {
                    var product = row.Product;
                    if (string.IsNullOrWhiteSpace(product.Handle))
                    {
                        continue;
                    }

                    if (product.ProductFamily?.Handle is string family
                        && !string.Equals(family, familyHandle, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    plans.Add(MapPlan(product));
                }

                if (batch.Count < ProductPageSize)
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
        catch (JsonException ex)
        {
            throw MapJsonException(ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            throw TransportFailure(ex);
        }

        return plans;
    }

    public async Task<SubscribeResult> SubscribeAsync(
        BillingCustomer customer,
        string? productHandle,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var handle = FirstNonEmpty(productHandle, _options.DefaultProductHandle);
        if (string.IsNullOrWhiteSpace(handle))
        {
            throw new BillingException(400, "A product handle is required.");
        }

        var maxioCustomer = await GetOrCreateCustomerAsync(customer, cancellationToken);
        var customerId = RequireCustomerId(maxioCustomer);
        var subscriptionReference = $"{customer.Reference}:{handle}";

        var existing = await FindExistingEnrollmentAsync(customerId, handle, subscriptionReference, cancellationToken);
        if (existing is not null)
        {
            return new SubscribeResult(existing, AlreadySubscribed: true);
        }

        try
        {
            var created = await Bounded(
                ct => _client.Subscriptions.CreateSubscription(
                    body: new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            ProductHandle = handle,
                            CustomerId = customerId,
                            CustomerReference = customer.Reference,
                            Reference = subscriptionReference,
                            PaymentCollectionMethod = CollectionMethod.Remittance
                        }
                    },
                    ct: ct),
                cancellationToken);

            var subscription = created.Subscription
                ?? throw new BillingException(502, "The billing provider returned a response that could not be processed.");
            return new SubscribeResult(MapSubscription(subscription), AlreadySubscribed: false);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            var recovered = await TryRecoverEnrollmentAsync(customerId, handle, subscriptionReference, cancellationToken);
            if (recovered is not null)
            {
                return new SubscribeResult(recovered, AlreadySubscribed: true);
            }

            throw MapCreateSubscriptionError(ex);
        }
        catch (MaxioWriteResendRefusedException)
        {
            var recovered = await TryRecoverEnrollmentAsync(customerId, handle, subscriptionReference, cancellationToken);
            if (recovered is not null)
            {
                return new SubscribeResult(recovered, AlreadySubscribed: true);
            }

            throw new BillingException(502, "The billing request may already have been received. Refresh your subscriptions and retry if it is not listed.");
        }
        catch (JsonException ex)
        {
            var recovered = await TryRecoverEnrollmentAsync(customerId, handle, subscriptionReference, cancellationToken);
            if (recovered is not null)
            {
                return new SubscribeResult(recovered, AlreadySubscribed: true);
            }

            throw MapJsonException(ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            var recovered = await TryRecoverEnrollmentAsync(customerId, handle, subscriptionReference, cancellationToken);
            if (recovered is not null)
            {
                return new SubscribeResult(recovered, AlreadySubscribed: true);
            }

            throw TransportFailure(ex);
        }
    }

    public async Task<IReadOnlyList<UserSubscription>> ListMySubscriptionsAsync(
        BillingCustomer customer,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();

        Customer? maxioCustomer;
        try
        {
            maxioCustomer = await LookupCustomerAsync(customer.Reference, cancellationToken);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return Array.Empty<UserSubscription>();
        }

        if (maxioCustomer is null)
        {
            return Array.Empty<UserSubscription>();
        }

        var customerId = RequireCustomerId(maxioCustomer);
        try
        {
            var rows = await ListCustomerSubscriptionsAsync(customerId, cancellationToken);
            return rows
                .Select(row => row.Subscription)
                .Where(sub => sub is not null)
                .Select(sub => MapSubscription(sub!))
                .ToList();
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return Array.Empty<UserSubscription>();
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRawError(ex.Error);
        }
        catch (JsonException ex)
        {
            throw MapJsonException(ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            throw TransportFailure(ex);
        }
    }

    private async Task<Customer> GetOrCreateCustomerAsync(BillingCustomer shopper, CancellationToken cancellationToken)
    {
        try
        {
            var existing = await LookupCustomerAsync(shopper.Reference, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            // First subscription for this shopper.
        }

        try
        {
            var created = await Bounded(
                ct => _client.Customers.CreateCustomer(
                    body: new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer
                        {
                            FirstName = shopper.FirstName,
                            LastName = shopper.LastName,
                            Email = shopper.Email,
                            Reference = shopper.Reference
                        }
                    },
                    ct: ct),
                cancellationToken);

            return created.Customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            var recovered = await TryLookupAfterCreateConflictAsync(shopper.Reference, cancellationToken);
            if (recovered is not null)
            {
                return recovered;
            }

            throw MapCreateCustomerError(ex);
        }
        catch (MaxioWriteResendRefusedException)
        {
            var recovered = await TryLookupAfterCreateConflictAsync(shopper.Reference, cancellationToken);
            if (recovered is not null)
            {
                return recovered;
            }

            throw new BillingException(502, "The billing request may already have been received. Refresh and retry.");
        }
        catch (JsonException ex)
        {
            var recovered = await TryLookupAfterCreateConflictAsync(shopper.Reference, cancellationToken);
            if (recovered is not null)
            {
                return recovered;
            }

            throw MapJsonException(ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            var recovered = await TryLookupAfterCreateConflictAsync(shopper.Reference, cancellationToken);
            if (recovered is not null)
            {
                return recovered;
            }

            throw TransportFailure(ex);
        }
    }

    private async Task<Customer?> LookupCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Bounded(
                ct => _client.Customers.ReadCustomerByReference(reference, ct: ct),
                cancellationToken);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRawError(ex.Error);
        }
        catch (JsonException ex)
        {
            throw MapJsonException(ex);
        }
    }

    private async Task<Customer?> TryLookupAfterCreateConflictAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            return await LookupCustomerAsync(reference, cancellationToken);
        }
        catch (BillingException)
        {
            return null;
        }
    }

    private async Task<UserSubscription?> FindExistingEnrollmentAsync(
        int customerId,
        string productHandle,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        var listed = await TryFindEnrolledAsync(customerId, productHandle, cancellationToken);
        if (listed is not null)
        {
            return listed;
        }

        return await FindByReferenceAsync(subscriptionReference, cancellationToken);
    }

    private async Task<UserSubscription?> TryRecoverEnrollmentAsync(
        int customerId,
        string productHandle,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        try
        {
            return await FindExistingEnrollmentAsync(customerId, productHandle, subscriptionReference, cancellationToken);
        }
        catch (BillingException)
        {
            return null;
        }
    }

    private async Task<UserSubscription?> TryFindEnrolledAsync(
        int customerId,
        string productHandle,
        CancellationToken cancellationToken)
    {
        try
        {
            var rows = await ListCustomerSubscriptionsAsync(customerId, cancellationToken);
            return FindEnrolled(rows, productHandle);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRawError(ex.Error);
        }
        catch (JsonException ex)
        {
            throw MapJsonException(ex);
        }
    }

    private Task<IReadOnlyList<SubscriptionResponse>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
        => Bounded(ct => _client.Customers.ListCustomerSubscriptions(customerId, ct: ct), cancellationToken);

    private async Task<UserSubscription?> FindByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Bounded(
                ct => _client.Subscriptions.FindSubscription(reference: reference, ct: ct),
                cancellationToken);
            var subscription = response.Subscription;
            if (subscription is null || !IsAlreadyEnrolled(subscription.State))
            {
                return null;
            }

            return MapSubscription(subscription);
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out RawError _))
            {
                return null;
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                if (raw.StatusCode == HttpStatusCode.NotFound)
                {
                    return null;
                }

                throw MapRawError(raw);
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var scope = MaxioHttpCallContext.BeginScope();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_options.CallBudget);
        return await call(cts.Token);
    }

    private static bool IsTransportFailure(Exception ex, CancellationToken cancellationToken)
        => ex is HttpRequestException
           || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested);

    private static BillingException TransportFailure(Exception ex)
        => new BillingException(503, "The billing provider is unavailable.", ex);

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey)
            || string.IsNullOrWhiteSpace(_options.Subdomain)
            || string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw new BillingException(503, "Subscription billing is not configured.");
        }
    }

    private static int RequireCustomerId(Customer customer)
    {
        if (customer.Id is int id)
        {
            return id;
        }

        throw new BillingException(502, "The billing provider returned a response that could not be processed.");
    }

    private static UserSubscription? FindEnrolled(IReadOnlyList<SubscriptionResponse> rows, string productHandle)
    {
        foreach (var row in rows)
        {
            var subscription = row.Subscription;
            if (subscription is null)
            {
                continue;
            }

            if (!string.Equals(subscription.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (IsAlreadyEnrolled(subscription.State))
            {
                return MapSubscription(subscription);
            }
        }

        return null;
    }

    private static bool IsAlreadyEnrolled(SubscriptionState? state)
    {
        if (state is null)
        {
            return true;
        }

        return state == SubscriptionState.Active
               || state == SubscriptionState.Assessing
               || state == SubscriptionState.Pending
               || state == SubscriptionState.Trialing
               || state == SubscriptionState.Paused
               || state == SubscriptionState.PastDue
               || state == SubscriptionState.SoftFailure
               || state == SubscriptionState.Unpaid
               || state == SubscriptionState.AwaitingSignup;
    }

    private static SubscriptionPlan MapPlan(Product product)
    {
        decimal price = product.PriceInCents is long cents ? cents / 100m : 0m;
        return new SubscriptionPlan(
            Handle: product.Handle ?? string.Empty,
            Name: product.Name ?? product.Handle ?? "Plan",
            Price: price,
            Interval: product.Interval,
            IntervalUnit: product.IntervalUnit?.Value);
    }

    private static UserSubscription MapSubscription(Subscription subscription)
    {
        var cents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents;
        return new UserSubscription(
            ProductHandle: subscription.Product?.Handle,
            ProductName: subscription.Product?.Name,
            Price: cents is long value ? value / 100m : null,
            State: subscription.State?.Value,
            NextBillingDate: subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
            Reference: subscription.Reference);
    }

    private BillingException MapListProductsError(SdkException<ListProductsForProductFamilyError> ex)
    {
        if (ex.Error.TryGetString(out var _))
        {
            return new BillingException(404, "No subscription plans are available.", ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return MapRawError(raw);
        }

        return new BillingException(503, "The billing provider is unavailable.", ex);
    }

    private BillingException MapCreateCustomerError(SdkException<CreateCustomerError> ex)
    {
        if (ex.Error.TryGetCustomerErrorResponse1(out var _))
        {
            _logger.LogWarning("Maxio rejected customer create (422).");
            return new BillingException(422, "The billing customer could not be created.", ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return MapRawError(raw);
        }

        return new BillingException(503, "The billing provider is unavailable.", ex);
    }

    private BillingException MapCreateSubscriptionError(SdkException<CreateSubscriptionError> ex)
    {
        if (ex.Error.TryGetErrorListResponse1(out var list))
        {
            var detail = list.Errors is { Count: > 0 }
                ? string.Join(" ", list.Errors)
                : "The subscription could not be created.";

            if (ContainsPaymentChallenge(detail))
            {
                return new BillingException(422, "This plan cannot be subscribed without a payment method.", ex);
            }

            return new BillingException(422, Truncate(detail), ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return MapRawError(raw);
        }

        return new BillingException(503, "The billing provider is unavailable.", ex);
    }

    private BillingException MapRawError(RawError raw)
    {
        var code = (int)raw.StatusCode;
        _logger.LogWarning("Maxio returned HTTP {StatusCode}", code);

        if (code is 401 or 403)
        {
            return new BillingException(503, "The billing provider is unavailable.");
        }

        if (code == 404)
        {
            return new BillingException(404, "The requested billing resource was not found.");
        }

        if (code is >= 400 and < 500)
        {
            return new BillingException(code, "The billing request was rejected.");
        }

        return new BillingException(503, "The billing provider is unavailable.");
    }

    private BillingException MapJsonException(JsonException ex)
    {
        var status = MaxioHttpCallContext.Current?.LastStatus;
        if (status is HttpStatusCode code && (int)code is >= 400 and < 500)
        {
            _logger.LogWarning(ex, "Maxio rejected the request (HTTP {StatusCode}) with an unreadable body.", (int)code);
            return new BillingException((int)code, "The billing request was rejected.", ex);
        }

        _logger.LogWarning(ex, "Maxio returned a response that could not be processed.");
        return new BillingException(502, "The billing provider returned a response that could not be processed.", ex);
    }

    private static bool ContainsPaymentChallenge(string detail)
        => detail.Contains("action_link", StringComparison.OrdinalIgnoreCase)
           || detail.Contains("3-d", StringComparison.OrdinalIgnoreCase)
           || detail.Contains("3d secure", StringComparison.OrdinalIgnoreCase)
           || detail.Contains("credit card", StringComparison.OrdinalIgnoreCase);

    private static string Truncate(string value, int max = 400)
        => value.Length <= max ? value : value[..max];

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}
