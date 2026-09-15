using System;
using System.Collections.Generic;
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
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class MaxioBillingService : ISubscriptionBillingService
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

        try
        {
            var family = await ResolveProductFamilyAsync(cancellationToken);
            // ProductFamily.Id is int? (records-3-Of-Su.md); ListProductsForProductFamily takes string productFamilyId (operations/ProductFamilies.md).
            var familyId = RequireValue(family.Id);
            var plans = new List<SubscriptionPlan>();
            var page = 1;

            while (true)
            {
                var batch = await CallAsync(
                    ct => _client.ProductFamilies.ListProductsForProductFamily(
                        productFamilyId: familyId.ToString(),
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

                foreach (var item in batch)
                {
                    plans.Add(MapPlan(item.Product));
                }

                if (batch.Count < ProductPageSize)
                {
                    break;
                }

                page++;
            }

            return plans;
        }
        catch (BillingException)
        {
            throw;
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            throw Translate(ex);
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex);
        }
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscribeToPlan command, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.ProductHandle))
        {
            throw new BillingException(400, "A product handle is required.");
        }

        try
        {
            await EnsureProductExistsAsync(command.ProductHandle, cancellationToken);
            var customer = await EnsureCustomerAsync(command, cancellationToken);
            // Customer.Id is int? (records-2-Cr-Ne.md); FindLiveSubscriptionAsync / ListCustomerSubscriptions take int (operations/Customers.md).
            var customerId = RequireValue(customer.Id);

            var existing = await FindLiveSubscriptionAsync(customerId, command.UserId, command.ProductHandle, cancellationToken);
            if (existing is not null)
            {
                return new SubscribeResult(MapSubscription(existing), Created: false);
            }

            return await CreateSubscriptionAsync(customer, command, cancellationToken);
        }
        catch (BillingException)
        {
            throw;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            throw Translate(ex);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            throw Translate(ex);
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            throw Translate(ex);
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex);
        }
    }

    public async Task<IReadOnlyList<UserSubscription>> ListSubscriptionsForUserAsync(string userId, CancellationToken cancellationToken)
    {
        EnsureConfigured();

        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new BillingException(400, "A user id is required.");
        }

        try
        {
            var customer = await TryReadCustomerByReferenceAsync(userId, cancellationToken);
            if (customer is null)
            {
                return Array.Empty<UserSubscription>();
            }

            var responses = await CallAsync(
                ct => _client.Customers.ListCustomerSubscriptions(customerId: RequireValue(customer.Id), ct: ct),
                cancellationToken);

            return responses
                .Select(item => item.Subscription)
                .Where(sub => sub is not null)
                .Select(sub => MapSubscription(sub!))
                .ToList();
        }
        catch (BillingException)
        {
            throw;
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex);
        }
    }

    private async Task<ProductFamily> ResolveProductFamilyAsync(CancellationToken cancellationToken)
    {
        var families = await CallAsync(
            ct => _client.ProductFamilies.ListProductFamilies(
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                ct: ct),
            cancellationToken);

        var handle = _options.ProductFamilyHandle;
        var match = families
            .Select(item => item.ProductFamily)
            .FirstOrDefault(family =>
                family is not null &&
                string.Equals(family.Handle, handle, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            throw new BillingException(404, "The configured subscription catalog was not found.");
        }

        return match;
    }

    private async Task EnsureProductExistsAsync(string productHandle, CancellationToken cancellationToken)
    {
        try
        {
            await CallAsync(
                ct => _client.Products.ReadProductByHandle(apiHandle: productHandle, ct: ct),
                cancellationToken);
        }
        catch (SdkException<RawError> ex) when ((int)ex.Error.StatusCode == 404)
        {
            throw new BillingException(404, "The requested subscription plan was not found.", ex);
        }
    }

    private async Task<Customer> EnsureCustomerAsync(SubscribeToPlan command, CancellationToken cancellationToken)
    {
        var existing = await TryReadCustomerByReferenceAsync(command.UserId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        using (OnceOnlyWriteScope.Begin())
        {
            try
            {
                var created = await CallAsync(
                    ct => _client.Customers.CreateCustomer(
                        body: new CreateCustomerRequest
                        {
                            Customer = new CreateCustomer
                            {
                                FirstName = command.FirstName,
                                LastName = command.LastName,
                                Email = command.Email,
                                Reference = command.UserId
                            }
                        },
                        ct: ct),
                    cancellationToken);

                return created.Customer;
            }
            catch (DuplicateProviderWriteException)
            {
                _logger.LogWarning("Customer create was blocked after the first send; reconciling by reference.");
            }
            catch (SdkException<CreateCustomerError> ex)
            {
                var raced = await TryReadCustomerByReferenceAsync(command.UserId, cancellationToken);
                if (raced is not null)
                {
                    return raced;
                }

                throw Translate(ex);
            }
            catch (BillingException ex) when (ex.StatusCode is 503 or 504)
            {
                var raced = await TryReadCustomerByReferenceAsync(command.UserId, cancellationToken);
                if (raced is not null)
                {
                    return raced;
                }

                throw;
            }
        }

        var afterWrite = await TryReadCustomerByReferenceAsync(command.UserId, cancellationToken);
        if (afterWrite is not null)
        {
            return afterWrite;
        }

        throw new BillingException(502, "The billing customer could not be created.");
    }

    private async Task<Customer?> TryReadCustomerByReferenceAsync(string userId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await CallAsync(
                ct => _client.Customers.ReadCustomerByReference(reference: userId, ct: ct),
                cancellationToken);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when ((int)ex.Error.StatusCode == 404)
        {
            return null;
        }
    }

    private async Task<Subscription?> FindLiveSubscriptionAsync(
        int customerId,
        string userId,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var listed = await CallAsync(
            ct => _client.Customers.ListCustomerSubscriptions(customerId: customerId, ct: ct),
            cancellationToken);

        foreach (var item in listed)
        {
            var subscription = item.Subscription;
            if (subscription is not null &&
                IsLive(subscription) &&
                string.Equals(subscription.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase))
            {
                return subscription;
            }
        }

        try
        {
            var found = await CallAsync(
                ct => _client.Subscriptions.FindSubscription(
                    reference: SubscriptionReference(userId, productHandle),
                    ct: ct),
                cancellationToken);

            if (found.Subscription is not null && IsLive(found.Subscription))
            {
                return found.Subscription;
            }
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                return null;
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw Translate(raw);
            }

            throw new BillingException(502, "The billing provider returned an error.", ex);
        }

        return null;
    }

    private async Task<SubscribeResult> CreateSubscriptionAsync(
        Customer customer,
        SubscribeToPlan command,
        CancellationToken cancellationToken)
    {
        using (OnceOnlyWriteScope.Begin())
        {
            try
            {
                var created = await CallAsync(
                    ct => _client.Subscriptions.CreateSubscription(
                        body: new CreateSubscriptionRequest
                        {
                            Subscription = new CreateSubscription
                            {
                                ProductHandle = command.ProductHandle,
                                CustomerId = customer.Id,
                                Reference = SubscriptionReference(command.UserId, command.ProductHandle),
                                PaymentCollectionMethod = CollectionMethod.Invoice
                            }
                        },
                        ct: ct),
                    cancellationToken);

                if (created.Subscription is null)
                {
                    throw new BillingException(502, "The billing provider returned a response that could not be processed.");
                }

                return new SubscribeResult(MapSubscription(created.Subscription), Created: true);
            }
            catch (DuplicateProviderWriteException)
            {
                _logger.LogWarning("Subscription create was blocked after the first send; reconciling.");
            }
            catch (SdkException<CreateSubscriptionError> ex)
            {
                var recovered = await FindLiveSubscriptionAsync(RequireValue(customer.Id), command.UserId, command.ProductHandle, cancellationToken);
                if (recovered is not null)
                {
                    return new SubscribeResult(MapSubscription(recovered), Created: false);
                }

                throw Translate(ex);
            }
            catch (BillingException ex) when (ex.StatusCode is 503 or 504)
            {
                var recovered = await FindLiveSubscriptionAsync(RequireValue(customer.Id), command.UserId, command.ProductHandle, cancellationToken);
                if (recovered is not null)
                {
                    return new SubscribeResult(MapSubscription(recovered), Created: false);
                }

                throw;
            }
        }

        var afterWrite = await FindLiveSubscriptionAsync(RequireValue(customer.Id), command.UserId, command.ProductHandle, cancellationToken);
        if (afterWrite is not null)
        {
            return new SubscribeResult(MapSubscription(afterWrite), Created: false);
        }

        throw new BillingException(502, "The subscription could not be confirmed after enrollment.");
    }

    private async Task<T> CallAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        MaxioCallStatus.LastStatusCode = null;

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(CallBudget);
            return await call(cts.Token);
        }
        catch (JsonException ex)
        {
            throw TranslateJson(ex);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new BillingException(504, "The billing provider timed out.");
        }
        catch (HttpRequestException ex)
        {
            throw new BillingException(503, "The billing provider is unreachable.", ex);
        }
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey) ||
            string.IsNullOrWhiteSpace(_options.Subdomain) ||
            string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw new BillingException(500, "Subscription billing is not configured.");
        }
    }

    private static string SubscriptionReference(string userId, string productHandle) =>
        $"eshop:{userId}:{productHandle}";

    private static bool IsLive(Subscription subscription)
    {
        var state = subscription.State;
        if (state is null)
        {
            return true;
        }

        return state != SubscriptionState.Canceled &&
               state != SubscriptionState.Expired &&
               state != SubscriptionState.FailedToCreate &&
               state != SubscriptionState.TrialEnded;
    }

    private static SubscriptionPlan MapPlan(Product product)
    {
        // Product.PriceInCents is long?, Product.Interval is int? (records-3-Of-Su.md).
        var price = ToMoney(RequireValue(product.PriceInCents));
        var intervalUnit = product.IntervalUnit?.Value ?? string.Empty;
        return new SubscriptionPlan(
            product.Handle ?? string.Empty,
            product.Name ?? string.Empty,
            product.Description,
            price,
            RequireValue(product.Interval),
            intervalUnit);
    }

    private static UserSubscription MapSubscription(Subscription subscription)
    {
        var product = subscription.Product;
        return new UserSubscription(
            RequireValue(subscription.Id),
            product?.Handle ?? string.Empty,
            product?.Name ?? string.Empty,
            ToMoney(product?.PriceInCents ?? 0),
            subscription.State?.Value ?? "unknown",
            subscription.CurrentPeriodEndsAt);
    }

    private static decimal ToMoney(long cents) => cents / 100m;

    private static int RequireValue(int? value) =>
        value ?? throw new BillingException(502, "The billing provider returned a response that could not be processed.");

    private static long RequireValue(long? value) =>
        value ?? throw new BillingException(502, "The billing provider returned a response that could not be processed.");

    private BillingException TranslateJson(JsonException ex)
    {
        var status = MaxioCallStatus.LastStatusCode;
        if (status is >= 400 and < 500)
        {
            _logger.LogWarning("Billing provider rejected a request with an unreadable error body (HTTP {StatusCode}).", status.Value);
            return new BillingException(status.Value, "The billing request was rejected.", ex);
        }

        _logger.LogWarning("Billing provider returned an unreadable success body.");
        return new BillingException(502, "The billing provider returned a response that could not be processed.", ex);
    }

    private BillingException Translate(SdkException<RawError> ex) => Translate(ex.Error);

    private BillingException Translate(RawError raw)
    {
        var status = (int)raw.StatusCode;
        _logger.LogWarning("Billing provider returned HTTP {StatusCode}.", status);

        if (status is >= 400 and < 500)
        {
            return new BillingException(status, "The billing request was rejected.");
        }

        return new BillingException(502, "The billing provider returned an error.");
    }

    private BillingException Translate(SdkException<ListProductsForProductFamilyError> ex)
    {
        if (ex.Error.TryGetString(out var message) && !string.IsNullOrWhiteSpace(message))
        {
            return new BillingException(404, "The configured subscription catalog was not found.", ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return Translate(raw);
        }

        return new BillingException(502, "The billing provider returned an error.", ex);
    }

    private BillingException Translate(SdkException<CreateCustomerError> ex)
    {
        if (ex.Error.TryGetCustomerErrorResponse1(out _))
        {
            return new BillingException(422, "The billing customer could not be created.", ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return Translate(raw);
        }

        return new BillingException(502, "The billing provider returned an error.", ex);
    }

    private BillingException Translate(SdkException<CreateSubscriptionError> ex)
    {
        if (ex.Error.TryGetErrorListResponse1(out var list) && list.Errors is { Count: > 0 })
        {
            var message = string.Join(" ", list.Errors.Where(item => !string.IsNullOrWhiteSpace(item)));
            if (string.IsNullOrWhiteSpace(message))
            {
                message = "The subscription could not be created.";
            }

            return new BillingException(422, message, ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return Translate(raw);
        }

        return new BillingException(502, "The billing provider returned an error.", ex);
    }

    private BillingException Translate(SdkException<FindSubscriptionError> ex)
    {
        if (ex.Error.TryGetNoContent(out var missing))
        {
            return Translate(missing);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return Translate(raw);
        }

        return new BillingException(502, "The billing provider returned an error.", ex);
    }
}
