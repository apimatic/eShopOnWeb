using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Billing;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly ConcurrentDictionary<string, byte> InFlightSubscriptions = new();
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan[] ConcurrentRetryDelays = { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4) };

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly IAppLogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        MaxioOptions options,
        IAppLogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken)
    {
        EnsureConfigured();

        var family = await FindProductFamilyAsync(cancellationToken);
        if (family?.Id is null)
        {
            throw new BillingException(404,
                $"The product family '{_options.ProductFamilyHandle}' was not found in Maxio.");
        }

        var products = await ExecuteAsync(token => _client.ProductFamilies.ListProductsForProductFamily(
            family.Id.Value.ToString(),
            null, null, null, null, null, null, null, null,
            page: 1,
            perPage: 100,
            ct: token), cancellationToken);

        return products.Select(MapPlan).ToList();
    }

    public async Task<SubscriptionSummary> SubscribeAsync(string userEmail, string planHandle, CancellationToken cancellationToken)
    {
        EnsureConfigured();

        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new BillingException(400, "A plan handle is required to subscribe.");
        }

        userEmail = NormalizeEmail(userEmail);
        planHandle = planHandle.Trim();

        var product = await ResolvePlanAsync(planHandle, cancellationToken);
        var customer = await GetOrCreateCustomerAsync(userEmail, cancellationToken);

        var subscriptionReference = SubscriptionReference(userEmail, planHandle);

        var startedByThisCall = InFlightSubscriptions.TryAdd(subscriptionReference, 0);
        try
        {
            var existing = startedByThisCall
                ? await TryFindSubscriptionAsync(subscriptionReference, cancellationToken)
                : await PollForExistingAsync(subscriptionReference, cancellationToken);

            if (existing is not null)
            {
                _logger.LogInformation($"Subscription {existing.Id} already exists for {userEmail} on {planHandle}.");
                return MapSubscription(existing);
            }

            SubscriptionResponse created;
            try
            {
                created = await ExecuteAsync(token => _client.Subscriptions.CreateSubscription(
                    new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            ProductHandle = planHandle,
                            CustomerId = customer.Id,
                            Reference = subscriptionReference,
                            PaymentCollectionMethod = CollectionMethod.Automatic
                        }
                    }, token), cancellationToken);
            }
            catch (BillingException ex) when (ex.StatusCode == 422 || (ex.StatusCode >= 500 && ex.StatusCode <= 599))
            {
                var reconciled = await TryFindSubscriptionAsync(subscriptionReference, cancellationToken);
                if (reconciled is not null)
                {
                    _logger.LogInformation($"Subscription create for {subscriptionReference} ended in a concurrent or ambiguous state; reconciled to existing subscription {reconciled.Id}.");
                    return MapSubscription(reconciled);
                }
                throw;
            }

            if (created.Subscription?.State == SubscriptionState.AwaitingSignup)
            {
                created = await ExecuteAsync(token => _client.Subscriptions.ActivateSubscription(
                    created.Subscription.Id!.Value, null, token), cancellationToken);
            }

            var effectiveCustomerId = created.Subscription?.Customer?.Id;
            if (effectiveCustomerId.HasValue && customer.Id.HasValue && effectiveCustomerId.Value != customer.Id.Value)
            {
                _logger.LogInformation($"Maxio attached subscription {created.Subscription?.Id} to customer {effectiveCustomerId.Value} instead of {customer.Id.Value}; the subscription reference still deduplicates enrollment.");
            }

            return MapSubscription(created);
        }
        finally
        {
            if (startedByThisCall)
            {
                InFlightSubscriptions.TryRemove(subscriptionReference, out _);
            }
        }
    }

    public async Task<IReadOnlyList<SubscriptionSummary>> ListForUserAsync(string userEmail, CancellationToken cancellationToken)
    {
        EnsureConfigured();

        userEmail = NormalizeEmail(userEmail);

        var customers = await FindCustomerRecordsForUserAsync(userEmail, cancellationToken);
        if (customers.Count == 0)
        {
            return new List<SubscriptionSummary>();
        }

        var seenSubscriptionIds = new HashSet<int>();
        var summaries = new List<SubscriptionSummary>();
        foreach (var customerId in customers.Distinct())
        {
            var subscriptions = await ExecuteAsync(
                token => _client.Customers.ListCustomerSubscriptions(customerId, token), cancellationToken);

            foreach (var subscription in subscriptions)
            {
                if (subscription.Subscription?.Id is { } id && seenSubscriptionIds.Add(id))
                {
                    summaries.Add(MapSubscription(subscription));
                }
            }
        }

        return summaries;
    }

    private async Task<List<int>> FindCustomerRecordsForUserAsync(string userEmail, CancellationToken cancellationToken)
    {
        var customerIds = new List<int>();

        try
        {
            var found = await ExecuteAsync(token => _client.Customers.ReadCustomerByReference(userEmail, token), cancellationToken);
            if (found.Customer?.Id is { } byReference)
            {
                customerIds.Add(byReference);
            }
        }
        catch (BillingException ex) when (ex.StatusCode == 404)
        {
        }

        var page = 1;
        const int perPage = 50;
        while (true)
        {
            IReadOnlyList<CustomerResponse> customers;
            try
            {
                customers = await ExecuteAsync(token => _client.Customers.ListCustomers(
                    null, null, null, null, null, null, userEmail, page, perPage, ct: token), cancellationToken);
            }
            catch (BillingException ex) when (ex.StatusCode == 404)
            {
                break;
            }

            customerIds.AddRange(customers
                .Where(response => string.Equals(response.Customer?.Email, userEmail, StringComparison.OrdinalIgnoreCase))
                .Select(response => response.Customer!.Id!.Value));

            if (customers.Count < perPage)
            {
                break;
            }
            page++;
        }

        return customerIds;
    }

    private void EnsureConfigured()
    {
        if (!_options.IsConfigured)
        {
            throw new BillingException(503, "Maxio billing is not configured on this environment.");
        }
    }

    private async Task<ProductFamily?> FindProductFamilyAsync(CancellationToken cancellationToken)
    {
        var families = await ExecuteAsync(token => _client.ProductFamilies.ListProductFamilies(
            null, null, null, null, null, ct: token), cancellationToken);

        return families.FirstOrDefault(f => f.ProductFamily?.Handle == _options.ProductFamilyHandle)?.ProductFamily;
    }

    private async Task<Product> ResolvePlanAsync(string planHandle, CancellationToken cancellationToken)
    {
        ProductResponse productResponse;
        try
        {
            productResponse = await ExecuteAsync(token => _client.Products.ReadProductByHandle(planHandle, token), cancellationToken);
        }
        catch (BillingException ex) when (ex.StatusCode == 404)
        {
            throw new BillingException(404, $"The subscription plan '{planHandle}' was not found.");
        }

        var product = productResponse.Product;
        if (product?.ProductFamily is not null &&
            !string.Equals(product.ProductFamily.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new BillingException(400, $"The plan '{planHandle}' does not belong to the configured product family.");
        }

        return product!;
    }

    private async Task<Customer> GetOrCreateCustomerAsync(string userEmail, CancellationToken cancellationToken)
    {
        var reference = userEmail;

        try
        {
            var found = await ExecuteAsync(token => _client.Customers.ReadCustomerByReference(reference, token), cancellationToken);
            return found.Customer!;
        }
        catch (BillingException ex) when (ex.StatusCode == 404)
        {
        }

        var request = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = FirstNameFromEmail(userEmail),
                LastName = "eShopOnWeb Shopper",
                Email = userEmail,
                Reference = reference
            }
        };

        try
        {
            var created = await ExecuteAsync(token => _client.Customers.CreateCustomer(request, token), cancellationToken);
            return created.Customer!;
        }
        catch (BillingException ex) when (ex.StatusCode == 422)
        {
            try
            {
                var found = await ExecuteAsync(token => _client.Customers.ReadCustomerByReference(reference, token), cancellationToken);
                return found.Customer!;
            }
            catch (BillingException)
            {
                throw new BillingException(ex.StatusCode,
                    $"Maxio rejected creating the billing customer for '{userEmail}': {ex.Message}", ex);
            }
        }
    }

    private async Task<Subscription?> TryFindSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(CallBudget);
        try
        {
            var response = await _client.Subscriptions.FindSubscription(reference, budget.Token);
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
                throw DescribeRaw(raw);
            }
            throw new BillingException(502, "Maxio returned an unexpected error while looking up the subscription.");
        }
        catch (JsonException ex)
        {
            throw DescribeUnreadableBody(ex);
        }
        catch (HttpRequestException ex)
        {
            throw new BillingException(503, "The Maxio API is unreachable right now. Please try again later.", ex);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new BillingException(503, "The call to the Maxio API timed out.");
        }
    }

    private async Task<Subscription?> PollForExistingAsync(string reference, CancellationToken cancellationToken)
    {
        foreach (var delay in ConcurrentRetryDelays)
        {
            var existing = await TryFindSubscriptionAsync(reference, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }
            await Task.Delay(delay, cancellationToken);
        }
        return null;
    }

    private async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(CallBudget);
        try
        {
            return await call(budget.Token);
        }
        catch (SdkException<RawError> ex)
        {
            throw DescribeRaw(ex.Error);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                throw new BillingException(422, "Maxio rejected the customer record. Check the account details and try again.");
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw DescribeRaw(raw);
            }
            throw new BillingException(502, "Maxio returned an unexpected error while creating the customer.");
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                var messages = errorList?.Errors is { Count: > 0 } errors
                    ? "Maxio rejected the subscription: " + string.Join("; ", errors)
                    : "Maxio rejected the subscription request.";
                throw new BillingException(422, messages);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw DescribeRaw(raw);
            }
            throw new BillingException(502, "Maxio returned an unexpected error while creating the subscription.");
        }
        catch (SdkException<ActivateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorArrayMapResponse1(out _))
            {
                throw new BillingException(400, "Maxio rejected the subscription activation.");
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw DescribeRaw(raw);
            }
            throw new BillingException(502, "Maxio returned an unexpected error while activating the subscription.");
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out _))
            {
                throw new BillingException(404, "Maxio rejected the product listing request for the configured product family.");
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw DescribeRaw(raw);
            }
            throw new BillingException(502, "Maxio returned an unexpected error while listing products.");
        }
        catch (JsonException ex)
        {
            throw DescribeUnreadableBody(ex);
        }
        catch (HttpRequestException ex)
        {
            throw new BillingException(503, "The Maxio API is unreachable right now. Please try again later.", ex);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new BillingException(503, "The call to the Maxio API timed out.");
        }
    }

    private BillingException DescribeUnreadableBody(JsonException inner)
    {
        var lastStatus = (int)MaxioLastStatusCodeHandler.LastStatus;
        if (lastStatus is >= 400 and < 500)
        {
            return new BillingException(lastStatus,
                "Maxio rejected the request (the error response could not be read).", inner);
        }
        return new BillingException(502,
            "The Maxio API returned a response that could not be processed; the outcome is unknown.", inner);
    }

    private static BillingException DescribeRaw(RawError raw)
    {
        var status = (int)raw.StatusCode;
        var detail = TryReadBody(raw);
        var message = string.IsNullOrWhiteSpace(detail)
            ? $"Maxio returned HTTP {status}."
            : $"Maxio returned HTTP {status}: {detail}";
        return new BillingException(status, message);
    }

    private static string? TryReadBody(RawError raw)
    {
        try
        {
            var body = raw.ReadAsString();
            if (string.IsNullOrWhiteSpace(body))
            {
                return null;
            }
            return body.Length <= 500 ? body : body[..500];
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static SubscriptionPlan MapPlan(ProductResponse response)
    {
        var product = response.Product;
        return new SubscriptionPlan(
            product.Handle ?? string.Empty,
            product.Name ?? string.Empty,
            (product.PriceInCents ?? 0) / 100m,
            product.PriceInCents ?? 0,
            product.Interval ?? 0,
            product.IntervalUnit?.Value ?? string.Empty,
            product.RequireCreditCard ?? false);
    }

    private static SubscriptionSummary MapSubscription(SubscriptionResponse response)
    {
        var subscription = response.Subscription
            ?? throw new BillingException(502, "Maxio returned a subscription response without a subscription body.");

        return MapSubscription(subscription);
    }

    private static SubscriptionSummary MapSubscription(Subscription subscription)
    {
        return new SubscriptionSummary(
            subscription.Id ?? 0,
            subscription.Product?.Handle ?? string.Empty,
            subscription.Product?.Name ?? string.Empty,
            subscription.State?.Value ?? string.Empty,
            (subscription.ProductPriceInCents ?? 0) / 100m,
            subscription.ProductPriceInCents ?? 0,
            subscription.CurrentPeriodEndsAt);
    }

    private static string NormalizeEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new BillingException(401, "A signed-in account is required.");
        }
        return email.Trim().ToLowerInvariant();
    }

    private static string SubscriptionReference(string userEmail, string planHandle) =>
        $"eshop-sub:{userEmail}:{planHandle}";

    private static string FirstNameFromEmail(string userEmail)
    {
        var localPart = userEmail.Split('@')[0];
        var separatorIndex = localPart.IndexOfAny(new[] { '.', '_', '-', '+' });
        var firstName = separatorIndex > 0 ? localPart[..separatorIndex] : localPart;
        return string.IsNullOrWhiteSpace(firstName) ? "eShop" : Capitalize(firstName);
    }

    private static string Capitalize(string value) =>
        char.ToUpperInvariant(value[0]) + value[1..];
}