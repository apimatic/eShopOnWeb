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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// <see cref="ISubscriptionBillingService"/> backed by Maxio Advanced Billing.
/// Idempotency: the Maxio customer is keyed by the caller's stable reference
/// (ReadCustomerByReference before any create), and a duplicate subscribe is
/// absorbed by matching an existing active subscription to the same plan.
/// </summary>
public class MaxioSubscriptionService : ISubscriptionBillingService
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var family = await FindProductFamilyAsync(cancellationToken);

        IReadOnlyList<ProductResponse> products;
        try
        {
            products = await Bounded(
                ct => _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: family.Id!.Value.ToString(),
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
                    ct: ct),
                cancellationToken);
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out var message))
            {
                _logger.LogWarning("Maxio ListProductsForProductFamily failed: {Message}", message);
            }
            else if (ex.Error.TryGetRawError(out var raw))
            {
                _logger.LogWarning("Maxio ListProductsForProductFamily failed: HTTP {Status} {Body}", (int)raw.StatusCode, raw.ReadAsString());
            }
            throw new BillingException("The billing provider could not return the plan catalog.");
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            throw Unreachable(ex);
        }

        return products
            .Where(p => p.Product is not null)
            .Select(p => MapPlan(p.Product!))
            .ToList();
    }

    public async Task<CustomerSubscription> SubscribeAsync(string customerReference, string email, string planHandle, CancellationToken cancellationToken = default)
    {
        var customer = await EnsureCustomerAsync(customerReference, email, cancellationToken);

        var existing = await ListCustomerSubscriptionsAsync(customer.Id!.Value, cancellationToken);
        var duplicate = existing.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase) &&
            s.State == SubscriptionState.Active);
        if (duplicate is not null)
        {
            return Map(duplicate);
        }

        var subscriptionReference = $"{customerReference}:{planHandle}";
        try
        {
            var created = await CreateSubscriptionAsync(customer.Id!.Value, planHandle, subscriptionReference, cancellationToken);
            return Map(created);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                var messages = string.Join("; ", errorList.Errors);
                _logger.LogWarning("Maxio rejected subscription for {Reference}: {Errors}", subscriptionReference, messages);
                throw new BillingException($"The billing provider rejected the subscription: {messages}", (int)HttpStatusCode.UnprocessableEntity);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                _logger.LogWarning("Maxio CreateSubscription failed: HTTP {Status} {Body}", (int)raw.StatusCode, raw.ReadAsString());
            }
            throw new BillingException("The billing provider rejected the subscription.");
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            // A transport failure on this non-idempotent POST means the write may already
            // have reached Maxio (the retry layer resends on connection failures). Settle
            // the outcome by re-reading provider state before reporting anything.
            var settled = await TryFindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
            if (settled is not null)
            {
                return Map(settled);
            }
            throw new BillingException(
                "The billing provider did not confirm the subscription; re-check 'my-subscriptions' before retrying.",
                ex,
                (int)HttpStatusCode.BadGateway);
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(string customerReference, CancellationToken cancellationToken = default)
    {
        Customer? customer;
        try
        {
            var response = await Bounded(
                ct => _client.Customers.ReadCustomerByReference(customerReference, ct: ct),
                cancellationToken);
            customer = response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            // No billing customer yet => no subscriptions.
            return Array.Empty<CustomerSubscription>();
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            throw Unreachable(ex);
        }

        if (customer?.Id is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await ListCustomerSubscriptionsAsync(customer.Id.Value, cancellationToken);
        return subscriptions.Select(Map).ToList();
    }

    /// <summary>
    /// Creates the subscription without any payment instrument. First attempt sends no
    /// payment fields at all (the intended path when the product/site does not require
    /// payment). If the site demands a payment method for the opening balance, retries
    /// with an invoice-style collection method — remittance (Relationship Invoicing
    /// sites), then invoice (legacy Statements sites) — which bills without a card.
    /// Every retry follows a 422, i.e. a rejected create, so nothing is duplicated.
    /// </summary>
    private async Task<Subscription> CreateSubscriptionAsync(int customerId, string planHandle, string reference, CancellationToken cancellationToken)
    {
        try
        {
            return await CreateSubscriptionCoreAsync(customerId, planHandle, reference, collectionMethod: null, cancellationToken);
        }
        catch (SdkException<CreateSubscriptionError> ex) when (DemandsPaymentMethod(ex))
        {
            _logger.LogInformation(
                "Maxio site requires a payment method for the opening balance; retrying subscription {Reference} with remittance collection.",
                reference);
        }

        try
        {
            return await CreateSubscriptionCoreAsync(customerId, planHandle, reference, CollectionMethod.Remittance, cancellationToken);
        }
        catch (SdkException<CreateSubscriptionError> ex) when (DemandsPaymentMethod(ex) || RejectsCollectionMethod(ex))
        {
            _logger.LogInformation(
                "Remittance collection rejected for subscription {Reference}; retrying with invoice collection (legacy Statements architecture).",
                reference);
        }

        return await CreateSubscriptionCoreAsync(customerId, planHandle, reference, CollectionMethod.Invoice, cancellationToken);
    }

    private async Task<Subscription> CreateSubscriptionCoreAsync(int customerId, string planHandle, string reference, CollectionMethod? collectionMethod, CancellationToken cancellationToken)
    {
        var created = await Bounded(
            ct => _client.Subscriptions.CreateSubscription(
                body: new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = planHandle,
                        CustomerId = customerId,
                        Reference = reference,
                        PaymentCollectionMethod = collectionMethod
                    }
                },
                ct: ct),
            cancellationToken);

        return created.Subscription
            ?? throw new BillingException("The billing provider returned an incomplete subscription record.");
    }

    private static bool DemandsPaymentMethod(SdkException<CreateSubscriptionError> ex) =>
        ex.Error.TryGetErrorListResponse1(out var errors) &&
        errors.Errors.Any(m => m.Contains("payment method", StringComparison.OrdinalIgnoreCase));

    private static bool RejectsCollectionMethod(SdkException<CreateSubscriptionError> ex) =>
        ex.Error.TryGetErrorListResponse1(out var errors) &&
        errors.Errors.Any(m => m.Contains("collection", StringComparison.OrdinalIgnoreCase));

    private async Task<ProductFamily> FindProductFamilyAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ProductFamilyResponse> families;
        try
        {
            families = await Bounded(
                ct => _client.ProductFamilies.ListProductFamilies(
                    dateField: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    ct: ct),
                cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            throw Unreachable(ex);
        }

        var family = families
            .Select(f => f.ProductFamily)
            .FirstOrDefault(f => string.Equals(f?.Handle, _settings.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase));

        if (family?.Id is null)
        {
            throw new BillingException(
                $"The billing provider has no product family with handle '{_settings.ProductFamilyHandle}'.");
        }
        return family;
    }

    private async Task<Customer> EnsureCustomerAsync(string reference, string email, CancellationToken cancellationToken)
    {
        try
        {
            var existing = await Bounded(
                ct => _client.Customers.ReadCustomerByReference(reference, ct: ct),
                cancellationToken);
            if (existing.Customer?.Id is not null)
            {
                return existing.Customer;
            }
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            // Expected on first subscribe — create below.
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            throw Unreachable(ex);
        }

        var (firstName, lastName) = SplitName(email);
        try
        {
            var created = await Bounded(
                ct => _client.Customers.CreateCustomer(
                    body: new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer
                        {
                            FirstName = firstName,
                            LastName = lastName,
                            Email = email,
                            Reference = reference
                        }
                    },
                    ct: ct),
                cancellationToken);

            if (created.Customer?.Id is null)
            {
                throw new BillingException("The billing provider returned an incomplete customer record.");
            }
            return created.Customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // CustomerErrorResponse1.Errors is a suspicious shared model — extract best-effort,
            // then fall back to the raw body.
            if (ex.Error.TryGetCustomerErrorResponse1(out var typed))
            {
                _logger.LogWarning("Maxio rejected customer {Reference}: {Detail}", reference, typed.Errors?.ToString());
            }
            else if (ex.Error.TryGetRawError(out var raw))
            {
                _logger.LogWarning("Maxio CreateCustomer failed: HTTP {Status} {Body}", (int)raw.StatusCode, raw.ReadAsString());
            }
            throw new BillingException("The billing provider rejected the customer record.", (int)HttpStatusCode.UnprocessableEntity);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            throw Unreachable(ex);
        }
    }

    private async Task<IReadOnlyList<Subscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Bounded(
                ct => _client.Customers.ListCustomerSubscriptions(customerId, ct: ct),
                cancellationToken);
            return response
                .Where(r => r.Subscription is not null)
                .Select(r => r.Subscription!)
                .ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            throw Unreachable(ex);
        }
    }

    private async Task<Subscription?> TryFindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var found = await Bounded(
                ct => _client.Subscriptions.FindSubscription(reference: reference, ct: ct),
                cancellationToken);
            return found.Subscription;
        }
        catch (SdkException<FindSubscriptionError> ex) when (ex.Error.TryGetNoContent(out _))
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Reconciliation probe for subscription {Reference} failed.", reference);
            return null;
        }
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        try
        {
            return await call(cts.Token);
        }
        catch (JsonException ex)
        {
            // A 2xx with a drifted body, or an error body that did not match the generated
            // error model — either way the SDK surfaces JsonException, not SdkException.
            _logger.LogWarning(ex, "Maxio returned a response that could not be deserialized.");
            throw new BillingException("The billing provider returned a response that could not be processed.", ex);
        }
    }

    private bool IsTransportFailure(Exception ex, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException;

    private BillingException Unreachable(Exception ex) =>
        new("The billing provider is unreachable or timed out.", ex, (int)HttpStatusCode.BadGateway);

    private BillingException Translate(SdkException<RawError> ex)
    {
        var status = (int)ex.Error.StatusCode;
        _logger.LogWarning("Maxio API error: HTTP {Status} {Body}", status, ex.Error.ReadAsString());
        return status is >= 400 and < 500
            ? new BillingException("The billing provider rejected the request.", ex, status)
            : new BillingException("The billing provider returned an error.", ex);
    }

    private static (string FirstName, string LastName) SplitName(string email)
    {
        var local = email.Split('@')[0];
        return string.IsNullOrWhiteSpace(local) ? ("Customer", "Unknown") : (local, "Customer");
    }

    private static SubscriptionPlan MapPlan(Product product) =>
        new(
            product.Name ?? string.Empty,
            product.Handle ?? string.Empty,
            product.PriceInCents ?? 0,
            product.Interval ?? 1,
            product.IntervalUnit?.Value ?? IntervalUnit.Month.Value);

    private static CustomerSubscription Map(Subscription subscription) =>
        new(
            subscription.Id ?? 0,
            subscription.Product?.Name ?? string.Empty,
            subscription.Product?.Handle ?? string.Empty,
            subscription.State?.Value ?? string.Empty,
            subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents,
            subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt);
}
