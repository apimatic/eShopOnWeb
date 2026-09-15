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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

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
        var familyId = FamilyId();
        var plans = new List<SubscriptionPlan>();
        var page = 1;
        const int perPage = 20;

        while (true)
        {
            IReadOnlyList<ProductResponse> batch;
            try
            {
                batch = await Bounded(
                    ct => GuardJson(() => _client.ProductFamilies.ListProductsForProductFamily(
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
                        perPage: perPage,
                        ct: ct)),
                    cancellationToken);
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                throw MapListProductsError(ex);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                throw Unavailable(ex);
            }

            foreach (var item in batch)
            {
                var product = item.Product;
                if (product is null || string.IsNullOrWhiteSpace(product.Handle))
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

    public async Task<SubscribeResult> SubscribeAsync(
        string buyerId,
        string email,
        string firstName,
        string lastName,
        string productHandle,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new BillingException("A product handle is required.", 400);
        }

        var plans = await ListPlansAsync(cancellationToken);
        if (!plans.Any(plan => string.Equals(plan.Handle, productHandle, StringComparison.OrdinalIgnoreCase)))
        {
            throw new BillingException("The requested subscription plan was not found.", 400);
        }

        var customer = await EnsureCustomerAsync(buyerId, email, firstName, lastName, cancellationToken);
        var reference = SubscriptionReference(buyerId, productHandle);

        var existing = await FindSubscriptionAsync(reference, cancellationToken);
        if (existing is not null && IsEnrolled(existing))
        {
            return new SubscribeResult(MapSubscription(existing), Created: false);
        }

        try
        {
            using (MaxioOnceWriteHandler.BeginWrite())
            {
                var created = await Bounded(
                    ct => GuardJson(() => _client.Subscriptions.CreateSubscription(
                        body: new CreateSubscriptionRequest
                        {
                            Subscription = new CreateSubscription
                            {
                                ProductHandle = productHandle,
                                CustomerId = customer.Id,
                                Reference = reference,
                                PaymentCollectionMethod = CollectionMethod.Remittance
                            }
                        },
                        ct: ct)),
                    cancellationToken);

                var subscription = created.Subscription
                    ?? throw new BillingException("The billing provider returned a response that could not be processed.", 502);
                return new SubscribeResult(MapSubscription(subscription), Created: true);
            }
        }
        catch (DuplicateWriteRefusedException ex)
        {
            return await ReconcileSubscribeAsync(reference, ex, cancellationToken);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            var recovered = await FindSubscriptionAsync(reference, cancellationToken);
            if (recovered is not null && IsEnrolled(recovered))
            {
                return new SubscribeResult(MapSubscription(recovered), Created: false);
            }

            throw MapCreateSubscriptionError(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return await ReconcileSubscribeAsync(reference, ex, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsForBuyerAsync(
        string buyerId,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var customer = await TryReadCustomerAsync(buyerId, cancellationToken);
        if (customer?.Id is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        try
        {
            var rows = await Bounded(
                ct => GuardJson(() => _client.Customers.ListCustomerSubscriptions(
                    customerId: customer.Id.Value,
                    ct: ct)),
                cancellationToken);

            return rows
                .Select(row => row.Subscription)
                .Where(subscription => subscription is not null)
                .Select(subscription => MapSubscription(subscription!))
                .ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRaw(ex.Error, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unavailable(ex);
        }
    }

    internal static string SubscriptionReference(string buyerId, string productHandle) =>
        $"{buyerId}:{productHandle}";

    private async Task<Customer> EnsureCustomerAsync(
        string buyerId,
        string email,
        string firstName,
        string lastName,
        CancellationToken cancellationToken)
    {
        var existing = await TryReadCustomerAsync(buyerId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            using (MaxioOnceWriteHandler.BeginWrite())
            {
                var created = await Bounded(
                    ct => GuardJson(() => _client.Customers.CreateCustomer(
                        body: new CreateCustomerRequest
                        {
                            Customer = new CreateCustomer
                            {
                                FirstName = firstName,
                                LastName = lastName,
                                Email = email,
                                Reference = buyerId
                            }
                        },
                        ct: ct)),
                    cancellationToken);

                return created.Customer;
            }
        }
        catch (DuplicateWriteRefusedException)
        {
            return await ReadCustomerOrThrowAsync(buyerId, cancellationToken);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                return await ReadCustomerOrThrowAsync(buyerId, cancellationToken);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw MapRaw(raw, ex);
            }

            throw new BillingException("The billing request was rejected.", 400, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            try
            {
                return await ReadCustomerOrThrowAsync(buyerId, cancellationToken);
            }
            catch (BillingException)
            {
                throw Unavailable(ex);
            }
        }
    }

    private async Task<Customer?> TryReadCustomerAsync(string buyerId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Bounded(
                ct => GuardJson(() => _client.Customers.ReadCustomerByReference(
                    reference: buyerId,
                    ct: ct)),
                cancellationToken);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRaw(ex.Error, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unavailable(ex);
        }
    }

    private async Task<Customer> ReadCustomerOrThrowAsync(string buyerId, CancellationToken cancellationToken)
    {
        var customer = await TryReadCustomerAsync(buyerId, cancellationToken);
        return customer ?? throw new BillingException("The billing request was rejected.", 409);
    }

    private async Task<Subscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Bounded(
                ct => GuardJson(() => _client.Subscriptions.FindSubscription(
                    reference: reference,
                    ct: ct)),
                cancellationToken);
            return response.Subscription;
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                return null;
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw MapRaw(raw, ex);
            }

            throw new BillingException("The billing provider is unavailable.", 503, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unavailable(ex);
        }
    }

    private async Task<SubscribeResult> ReconcileSubscribeAsync(
        string reference,
        Exception inner,
        CancellationToken cancellationToken)
    {
        var recovered = await FindSubscriptionAsync(reference, cancellationToken);
        if (recovered is not null && IsEnrolled(recovered))
        {
            return new SubscribeResult(MapSubscription(recovered), Created: false);
        }

        throw Unavailable(inner);
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private static async Task<T> GuardJson<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (JsonException ex)
        {
            throw MapJsonException(ex);
        }
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey) ||
            (string.IsNullOrWhiteSpace(_options.Subdomain) && string.IsNullOrWhiteSpace(_options.BaseUrl)) ||
            string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw new BillingException("Billing is not configured.", 503);
        }
    }

    private string FamilyId() => "handle:" + _options.ProductFamilyHandle;

    private static SubscriptionPlan MapPlan(Product product)
    {
        var cents = product.PriceInCents ?? 0;
        return new SubscriptionPlan(
            Handle: product.Handle!,
            Name: product.Name ?? product.Handle!,
            PriceInCents: cents,
            Price: cents / 100m,
            Interval: product.Interval ?? 1,
            IntervalUnit: product.IntervalUnit?.Value ?? IntervalUnit.Month.Value,
            RequireCreditCard: product.RequireCreditCard ?? false);
    }

    private static CustomerSubscription MapSubscription(Subscription subscription)
    {
        var cents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? 0;
        return new CustomerSubscription(
            Id: subscription.Id ?? 0,
            ProductHandle: subscription.Product?.Handle ?? string.Empty,
            ProductName: subscription.Product?.Name ?? string.Empty,
            PriceInCents: cents,
            Price: cents / 100m,
            State: subscription.State?.Value ?? string.Empty,
            CurrentPeriodEndsAt: subscription.CurrentPeriodEndsAt,
            NextBillingAt: subscription.NextAssessmentAt,
            Reference: subscription.Reference);
    }

    private static bool IsEnrolled(Subscription subscription)
    {
        var state = subscription.State;
        if (state is null)
        {
            return true;
        }

        return state != SubscriptionState.Canceled
            && state != SubscriptionState.Expired
            && state != SubscriptionState.FailedToCreate
            && state != SubscriptionState.TrialEnded;
    }

    private BillingException MapListProductsError(SdkException<ListProductsForProductFamilyError> ex)
    {
        if (ex.Error.TryGetString(out _))
        {
            _logger.LogWarning("Maxio product family was not found.");
            return new BillingException("Subscription plans are not available.", 502, ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return MapRaw(raw, ex);
        }

        return new BillingException("The billing provider is unavailable.", 503, ex);
    }

    private BillingException MapCreateSubscriptionError(SdkException<CreateSubscriptionError> ex)
    {
        if (ex.Error.TryGetErrorListResponse1(out var errors))
        {
            _logger.LogWarning("Maxio rejected CreateSubscription: {Errors}",
                errors.Errors is { Count: > 0 } ? string.Join("; ", errors.Errors) : "(empty)");
            var message = errors.Errors is { Count: > 0 }
                ? "The subscription could not be created."
                : "The subscription could not be created.";
            return new BillingException(message, 400, ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return MapRaw(raw, ex);
        }

        return new BillingException("The subscription could not be created.", 400, ex);
    }

    private static BillingException MapJsonException(JsonException ex)
    {
        var status = MaxioStatusCaptureHandler.LastStatusCode;
        if (status is { } code && (int)code >= 400)
        {
            var mapped = (int)code >= 500 ? 503 : 400;
            return new BillingException("The billing request was rejected.", mapped, ex);
        }

        return new BillingException("The billing provider returned a response that could not be processed.", 502, ex);
    }

    private static BillingException MapRaw(RawError raw, Exception inner)
    {
        var status = (int)raw.StatusCode;
        if (status is 401 or 403)
        {
            return new BillingException("The billing provider rejected the request.", 502, inner);
        }

        if (status >= 400 && status < 500)
        {
            return new BillingException("The billing request was rejected.", status == 404 ? 404 : 400, inner);
        }

        return Unavailable(inner);
    }

    private static BillingException Unavailable(Exception inner) =>
        new("The billing provider is unavailable.", 503, inner);
}
