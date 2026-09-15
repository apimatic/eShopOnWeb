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
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public class MaxioBillingService : ISubscriptionBillingService
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private const int ProductPageSize = 20;

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly IAppLogger<MaxioBillingService> _logger;

    public MaxioBillingService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioOptions> options,
        IAppLogger<MaxioBillingService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var familyId = ProductFamilyPath();
        var plans = new List<SubscriptionPlan>();
        var page = 1;

        while (true)
        {
            var responses = await Invoke(
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
                    perPage: ProductPageSize,
                    ct: ct),
                cancellationToken);

            var batch = responses
                .Select(r => r.Product)
                .Where(p => p.ArchivedAt is null)
                .Select(MapPlan)
                .ToList();

            plans.AddRange(batch);
            if (responses.Count < ProductPageSize)
            {
                break;
            }

            page++;
        }

        return plans;
    }

    public async Task<ShopperSubscription> SubscribeAsync(
        string userName,
        string email,
        string productHandle,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new BillingProviderException(400, "A productHandle is required.");
        }

        var handle = productHandle.Trim();
        var plan = await RequirePlanAsync(handle, cancellationToken);
        var customer = await EnsureCustomerAsync(userName, email, cancellationToken);
        var subscriptionReference = BuildSubscriptionReference(userName, handle);

        var existing = await FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
        if (existing is not null)
        {
            return MapSubscription(existing, created: false);
        }

        var customerId = RequireCustomerId(customer);

        existing = await FindActiveSubscriptionForProductAsync(customerId, handle, cancellationToken);
        if (existing is not null)
        {
            return MapSubscription(existing, created: false);
        }

        var created = await CreateSubscriptionWithReconcileAsync(
            customerId,
            handle,
            subscriptionReference,
            cancellationToken);

        return MapSubscription(created, created: true, fallbackPlan: plan);
    }

    public async Task<IReadOnlyList<ShopperSubscription>> ListMySubscriptionsAsync(
        string userName,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        Customer customer;
        try
        {
            customer = await ReadCustomerByReferenceAsync(userName, cancellationToken);
        }
        catch (BillingProviderException ex) when (ex.StatusCode == 404)
        {
            return Array.Empty<ShopperSubscription>();
        }

        var responses = await Invoke(
            ct => _client.Customers.ListCustomerSubscriptions(customerId: RequireCustomerId(customer), ct: ct),
            cancellationToken);

        return responses
            .Select(r => r.Subscription)
            .Where(s => s is not null)
            .Select(s => MapSubscription(s!, created: false))
            .ToList();
    }

    private async Task<SubscriptionPlan> RequirePlanAsync(string productHandle, CancellationToken cancellationToken)
    {
        var plans = await ListPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p =>
            string.Equals(p.Handle, productHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new BillingProviderException(400, "Unknown subscription plan.");
        }

        return plan;
    }

    private async Task<Customer> EnsureCustomerAsync(string userName, string email, CancellationToken cancellationToken)
    {
        try
        {
            return await ReadCustomerByReferenceAsync(userName, cancellationToken);
        }
        catch (BillingProviderException ex) when (ex.StatusCode == 404)
        {
            return await CreateCustomerWithReconcileAsync(userName, email, cancellationToken);
        }
    }

    private async Task<Customer> ReadCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Invoke(
                ct => _client.Customers.ReadCustomerByReference(reference: reference, ct: ct),
                cancellationToken);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when ((int)ex.Error.StatusCode == (int)HttpStatusCode.NotFound)
        {
            throw new BillingProviderException(404, "Billing customer was not found.", ex);
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRaw(ex.Error, "Unable to read the billing customer.");
        }
    }

    private async Task<Customer> CreateCustomerWithReconcileAsync(
        string userName,
        string email,
        CancellationToken cancellationToken)
    {
        var (firstName, lastName) = SplitName(userName, email);
        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = string.IsNullOrWhiteSpace(email) ? userName : email,
                Reference = userName
            }
        };

        using (MaxioWriteOnceScope.Begin())
        {
            try
            {
                var created = await Invoke(
                    ct => _client.Customers.CreateCustomer(body: body, ct: ct),
                    cancellationToken);
                return created.Customer;
            }
            catch (MaxioWriteAlreadySentException ex)
            {
                _logger.LogWarning("CreateCustomer write outcome unknown; reconciling by reference for {User}.", userName);
                return await ReconcileCustomerAsync(userName, ex, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                _logger.LogWarning("CreateCustomer transport failure; reconciling by reference for {User}.", userName);
                return await ReconcileCustomerAsync(userName, ex, cancellationToken);
            }
            catch (SdkException<CreateCustomerError> ex)
            {
                if (ex.Error.TryGetCustomerErrorResponse1(out _))
                {
                    try
                    {
                        return await ReadCustomerByReferenceAsync(userName, cancellationToken);
                    }
                    catch (BillingProviderException)
                    {
                        throw MapCreateCustomer(ex);
                    }
                }

                if (ex.Error.TryGetRawError(out var raw))
                {
                    if ((int)raw.StatusCode == (int)HttpStatusCode.UnprocessableEntity)
                    {
                        try
                        {
                            return await ReadCustomerByReferenceAsync(userName, cancellationToken);
                        }
                        catch (BillingProviderException)
                        {
                            throw MapRaw(raw, "The billing provider rejected the customer.");
                        }
                    }

                    throw MapRaw(raw, "Unable to create the billing customer.");
                }

                throw MapCreateCustomer(ex);
            }
        }
    }

    private async Task<Customer> ReconcileCustomerAsync(string userName, Exception inner, CancellationToken cancellationToken)
    {
        try
        {
            return await ReadCustomerByReferenceAsync(userName, cancellationToken);
        }
        catch (BillingProviderException)
        {
            throw new BillingProviderException(
                503,
                "The billing provider did not confirm the customer. Retry after checking account state.",
                inner);
        }
    }

    private async Task<Subscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Invoke(
                ct => _client.Subscriptions.FindSubscription(reference: reference, ct: ct),
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
                if ((int)raw.StatusCode == (int)HttpStatusCode.NotFound)
                {
                    return null;
                }

                throw MapRaw(raw, "Unable to look up the subscription.");
            }

            throw new BillingProviderException(502, "Unable to look up the subscription.", ex);
        }
    }

    private async Task<Subscription?> FindActiveSubscriptionForProductAsync(
        int customerId,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var responses = await Invoke(
            ct => _client.Customers.ListCustomerSubscriptions(customerId: customerId, ct: ct),
            cancellationToken);

        return responses
            .Select(r => r.Subscription)
            .Where(s => s is not null)
            .FirstOrDefault(s =>
                string.Equals(s!.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase)
                && IsOpenState(s.State));
    }

    private async Task<Subscription> CreateSubscriptionWithReconcileAsync(
        int customerId,
        string productHandle,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        var body = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = productHandle,
                CustomerId = customerId,
                Reference = subscriptionReference,
                PaymentCollectionMethod = CollectionMethod.Remittance
            }
        };

        using (MaxioWriteOnceScope.Begin())
        {
            try
            {
                var created = await Invoke(
                    ct => _client.Subscriptions.CreateSubscription(body: body, ct: ct),
                    cancellationToken);
                if (created.Subscription is null)
                {
                    throw new BillingProviderException(502, "The billing provider returned a response that could not be processed.");
                }

                return created.Subscription;
            }
            catch (MaxioWriteAlreadySentException ex)
            {
                _logger.LogWarning("CreateSubscription write outcome unknown; reconciling by reference {Reference}.", subscriptionReference);
                return await ReconcileSubscriptionAsync(subscriptionReference, ex, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                _logger.LogWarning("CreateSubscription transport failure; reconciling by reference {Reference}.", subscriptionReference);
                return await ReconcileSubscriptionAsync(subscriptionReference, ex, cancellationToken);
            }
            catch (SdkException<CreateSubscriptionError> ex)
            {
                var existing = await FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
                if (existing is not null)
                {
                    return existing;
                }

                throw MapCreateSubscription(ex);
            }
        }
    }

    private async Task<Subscription> ReconcileSubscriptionAsync(
        string subscriptionReference,
        Exception inner,
        CancellationToken cancellationToken)
    {
        var existing = await FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        throw new BillingProviderException(
            503,
            "The billing provider did not confirm the subscription. Retry after checking account state.",
            inner);
    }

    private async Task<T> Invoke<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        MaxioLastHttpStatus.Value = null;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);

        try
        {
            return await call(cts.Token);
        }
        catch (BillingProviderException)
        {
            throw;
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            throw MapListProductsForFamily(ex);
        }
        catch (JsonException ex)
        {
            throw MapJsonException(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new BillingProviderException(503, "The billing provider is unreachable.", ex);
        }
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey)
            || string.IsNullOrWhiteSpace(_options.Subdomain)
            || string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw new BillingProviderException(503, "Billing is not configured.");
        }
    }

    private string ProductFamilyPath() => $"handle:{_options.ProductFamilyHandle}";

    private static string BuildSubscriptionReference(string userName, string productHandle) =>
        $"{userName}:{productHandle}";

    private static int RequireCustomerId(Customer customer)
    {
        if (customer.Id is not int id)
        {
            throw new BillingProviderException(
                502,
                "The billing provider returned a response that could not be processed.");
        }

        return id;
    }

    private static (string FirstName, string LastName) SplitName(string userName, string email)
    {
        var source = string.IsNullOrWhiteSpace(email) ? userName : email;
        var local = source.Split('@')[0];
        var first = string.IsNullOrWhiteSpace(local) ? "Shopper" : local;
        return (first, "Shopper");
    }

    private static bool IsOpenState(SubscriptionState? state)
    {
        if (state is null)
        {
            return false;
        }

        return state == SubscriptionState.Active
            || state == SubscriptionState.Trialing
            || state == SubscriptionState.PastDue
            || state == SubscriptionState.Assessing
            || state == SubscriptionState.SoftFailure
            || state == SubscriptionState.Pending
            || state == SubscriptionState.AwaitingSignup
            || state == SubscriptionState.Paused
            || state == SubscriptionState.OnHold
            || state == SubscriptionState.Unpaid;
    }

    private static SubscriptionPlan MapPlan(Product product)
    {
        return new SubscriptionPlan
        {
            Id = product.Id ?? 0,
            Handle = product.Handle ?? string.Empty,
            Name = product.Name ?? string.Empty,
            Description = product.Description,
            Price = CentsToAmount(product.PriceInCents),
            Currency = "USD",
            Interval = product.Interval ?? 0,
            IntervalUnit = product.IntervalUnit?.Value ?? string.Empty
        };
    }

    private static ShopperSubscription MapSubscription(
        Subscription subscription,
        bool created,
        SubscriptionPlan? fallbackPlan = null)
    {
        return new ShopperSubscription
        {
            Id = subscription.Id ?? 0,
            Reference = subscription.Reference,
            ProductHandle = subscription.Product?.Handle ?? fallbackPlan?.Handle ?? string.Empty,
            ProductName = subscription.Product?.Name ?? fallbackPlan?.Name ?? string.Empty,
            Price = CentsToAmount(subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents),
            Currency = subscription.Currency ?? "USD",
            State = subscription.State?.Value ?? string.Empty,
            NextBillingAt = subscription.NextAssessmentAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            Created = created
        };
    }

    private static decimal CentsToAmount(long? cents) => (cents ?? 0) / 100m;

    private static decimal CentsToAmount(int? cents) => (cents ?? 0) / 100m;

    private BillingProviderException MapJsonException(JsonException ex)
    {
        var status = MaxioLastHttpStatus.Value;
        if (status is not null && (int)status.Value >= 400 && (int)status.Value < 500)
        {
            return new BillingProviderException((int)status.Value, "The billing provider rejected the request.", ex);
        }

        return new BillingProviderException(502, "The billing provider returned a response that could not be processed.", ex);
    }

    private static BillingProviderException MapRaw(RawError raw, string fallback)
    {
        var status = (int)raw.StatusCode;
        if (status >= 400 && status < 500)
        {
            return new BillingProviderException(status, fallback);
        }

        return new BillingProviderException(status >= 500 ? status : 502, fallback);
    }

    private static BillingProviderException MapCreateCustomer(SdkException<CreateCustomerError> ex)
    {
        if (ex.Error.TryGetCustomerErrorResponse1(out _))
        {
            return new BillingProviderException(422, "The billing provider rejected the customer.");
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return MapRaw(raw, "Unable to create the billing customer.");
        }

        return new BillingProviderException(502, "Unable to create the billing customer.", ex);
    }

    private static BillingProviderException MapCreateSubscription(SdkException<CreateSubscriptionError> ex)
    {
        if (ex.Error.TryGetErrorListResponse1(out var list) && list.Errors is { Count: > 0 })
        {
            return new BillingProviderException(422, string.Join(" ", list.Errors));
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return MapRaw(raw, "The billing provider rejected the subscription.");
        }

        return new BillingProviderException(422, "The billing provider rejected the subscription.", ex);
    }

    private static BillingProviderException MapListProductsForFamily(SdkException<ListProductsForProductFamilyError> ex)
    {
        if (ex.Error.TryGetString(out var message) && !string.IsNullOrWhiteSpace(message))
        {
            return new BillingProviderException(404, "The configured product family was not found.");
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return MapRaw(raw, "Unable to list subscription plans.");
        }

        return new BillingProviderException(502, "Unable to list subscription plans.", ex);
    }
}
