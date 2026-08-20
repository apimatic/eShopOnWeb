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

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
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

        const int perPage = 20;
        var page = 1;
        while (true)
        {
            IReadOnlyList<ProductResponse> batch;
            try
            {
                batch = await BoundedAsync(
                    ct => _client.Products.ListProducts(
                        dateField: null,
                        filter: null,
                        endDate: null,
                        endDatetime: null,
                        startDate: null,
                        startDatetime: null,
                        includeArchived: false,
                        include: null,
                        page: page,
                        perPage: perPage,
                        ct: ct),
                    cancellationToken);
            }
            catch (SdkException<RawError> ex)
            {
                throw MapRaw(ex.Error, "Subscription plans could not be loaded.");
            }

            foreach (var envelope in batch)
            {
                var product = envelope.Product;
                if (!string.IsNullOrWhiteSpace(familyHandle)
                    && product.ProductFamily is not null
                    && !string.Equals(product.ProductFamily.Handle, familyHandle, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var mapped = MapPlan(product);
                if (mapped is not null)
                {
                    plans.Add(mapped);
                }
            }

            if (batch.Count < perPage)
            {
                break;
            }

            page++;
        }

        return plans;
    }

    public async Task<ShopperSubscription> SubscribeAsync(string userName, string? productHandle, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new BillingException(401, "A signed-in shopper is required to subscribe.");
        }

        var handle = await ResolveProductHandleAsync(productHandle, cancellationToken);
        var customer = await EnsureCustomerAsync(userName, cancellationToken);
        var customerId = RequireId(customer.Id);
        var subscriptionReference = BuildSubscriptionReference(userName, handle);

        var existingByReference = await TryFindSubscriptionAsync(subscriptionReference, cancellationToken);
        if (existingByReference is not null)
        {
            if (IsOpen(existingByReference))
            {
                return MapSubscription(existingByReference);
            }

            if (CanReactivate(existingByReference))
            {
                return await ReactivateAsync(RequireId(existingByReference.Id), cancellationToken);
            }
        }

        var existingForProduct = await FindOpenSubscriptionForProductAsync(customerId, handle, cancellationToken);
        if (existingForProduct is not null)
        {
            return MapSubscription(existingForProduct);
        }

        try
        {
            var created = await WriteAsync(
                ct => _client.Subscriptions.CreateSubscription(
                    body: new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            ProductHandle = handle,
                            CustomerId = customerId,
                            Reference = subscriptionReference,
                            PaymentCollectionMethod = CollectionMethod.Invoice
                        }
                    },
                    ct: ct),
                cancellationToken);

            if (created.Subscription is null)
            {
                throw new BillingException(502, "The billing provider returned a response that could not be processed.");
            }

            return MapSubscription(created.Subscription);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            var recovered = await TryFindSubscriptionAsync(subscriptionReference, cancellationToken)
                            ?? await FindOpenSubscriptionForProductAsync(customerId, handle, cancellationToken);
            if (recovered is not null && IsOpen(recovered))
            {
                return MapSubscription(recovered);
            }

            throw MapCreateSubscription(ex);
        }
        catch (MaxioDuplicateWriteException)
        {
            var recovered = await TryFindSubscriptionAsync(subscriptionReference, cancellationToken)
                            ?? await FindOpenSubscriptionForProductAsync(customerId, handle, cancellationToken);
            if (recovered is not null)
            {
                return MapSubscription(recovered);
            }

            throw new BillingException(503, "The billing provider did not confirm the subscription. Check your account before retrying.");
        }
    }

    public async Task<IReadOnlyList<ShopperSubscription>> GetSubscriptionsForUserAsync(string userName, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new BillingException(401, "A signed-in shopper is required to view subscriptions.");
        }

        var customer = await TryReadCustomerByReferenceAsync(userName, cancellationToken);
        if (customer?.Id is not int customerId)
        {
            return Array.Empty<ShopperSubscription>();
        }

        try
        {
            var envelopes = await BoundedAsync(
                ct => _client.Customers.ListCustomerSubscriptions(customerId, ct: ct),
                cancellationToken);

            return envelopes
                .Select(e => e.Subscription)
                .Where(s => s is not null)
                .Select(s => MapSubscription(s!))
                .ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRaw(ex.Error, "The shopper's subscriptions could not be loaded.");
        }
    }

    private async Task<Customer> EnsureCustomerAsync(string userName, CancellationToken cancellationToken)
    {
        var existing = await TryReadCustomerByReferenceAsync(userName, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName, email) = SplitIdentity(userName);
        try
        {
            var created = await WriteAsync(
                ct => _client.Customers.CreateCustomer(
                    body: new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer
                        {
                            FirstName = firstName,
                            LastName = lastName,
                            Email = email,
                            Reference = userName
                        }
                    },
                    ct: ct),
                cancellationToken);

            return created.Customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            var recovered = await TryReadCustomerByReferenceAsync(userName, cancellationToken);
            if (recovered is not null)
            {
                return recovered;
            }

            throw MapCreateCustomer(ex);
        }
        catch (MaxioDuplicateWriteException)
        {
            var recovered = await TryReadCustomerByReferenceAsync(userName, cancellationToken);
            if (recovered is not null)
            {
                return recovered;
            }

            throw new BillingException(503, "The billing provider did not confirm the customer. Check your account before retrying.");
        }
    }

    private async Task<Customer?> TryReadCustomerByReferenceAsync(string userName, CancellationToken cancellationToken)
    {
        try
        {
            var response = await BoundedAsync(
                ct => _client.Customers.ReadCustomerByReference(userName, ct: ct),
                cancellationToken);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when ((int)ex.Error.StatusCode == 404)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRaw(ex.Error, "The billing customer could not be loaded.");
        }
    }

    private async Task<Subscription?> TryFindSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await BoundedAsync(
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
                if ((int)raw.StatusCode == 404)
                {
                    return null;
                }

                throw MapRaw(raw, "The existing subscription could not be loaded.");
            }

            throw new BillingException(502, "The existing subscription could not be loaded.");
        }
    }

    private async Task<Subscription?> FindOpenSubscriptionForProductAsync(int customerId, string productHandle, CancellationToken cancellationToken)
    {
        try
        {
            var envelopes = await BoundedAsync(
                ct => _client.Customers.ListCustomerSubscriptions(customerId, ct: ct),
                cancellationToken);

            return envelopes
                .Select(e => e.Subscription)
                .FirstOrDefault(s => s is not null && IsOpen(s) && string.Equals(s.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase));
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRaw(ex.Error, "The shopper's subscriptions could not be loaded.");
        }
    }

    private async Task<ShopperSubscription> ReactivateAsync(int subscriptionId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await WriteAsync(
                ct => _client.SubscriptionStatus.ReactivateSubscription(subscriptionId, body: null, ct: ct),
                cancellationToken);

            if (response.Subscription is null)
            {
                throw new BillingException(502, "The billing provider returned a response that could not be processed.");
            }

            return MapSubscription(response.Subscription);
        }
        catch (SdkException<ReactivateSubscriptionError> ex)
        {
            throw MapReactivate(ex);
        }
    }

    private async Task<string> ResolveProductHandleAsync(string? productHandle, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(productHandle))
        {
            return productHandle.Trim();
        }

        var plans = await ListPlansAsync(cancellationToken);
        var first = plans.FirstOrDefault();
        if (first is null)
        {
            throw new BillingException(404, "No subscription plans are available.");
        }

        return first.Handle;
    }

    private async Task<T> BoundedAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(CallBudget);
        MaxioHttpStatusHolder.LastStatus = null;

        try
        {
            return await call(linked.Token);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(
                "Maxio JSON error (HTTP {Status}): {Message}",
                MaxioHttpStatusHolder.LastStatus?.ToString() ?? "none",
                ex.Message);
            throw MapJsonException(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning("Maxio billing call failed: {Message}", ex.Message);
            throw new BillingException(503, "The billing provider is unreachable. Please try again.", ex);
        }
    }

    private async Task<T> WriteAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        MaxioWriteGuard.Reset();
        return await BoundedAsync(call, cancellationToken);
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new BillingException(500, "Billing is not configured.");
        }

        if (string.IsNullOrWhiteSpace(_options.Subdomain) && string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            throw new BillingException(500, "Billing is not configured.");
        }
    }

    private static BillingException MapJsonException(JsonException ex)
    {
        var status = MaxioHttpStatusHolder.LastStatus;
        if (status is >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError)
        {
            return new BillingException((int)status.Value, "The billing provider rejected the request.", ex);
        }

        return new BillingException(502, "The billing provider returned a response that could not be processed.", ex);
    }

    private static BillingException MapCreateCustomer(SdkException<CreateCustomerError> ex)
    {
        if (ex.Error.TryGetCustomerErrorResponse1(out _))
        {
            return new BillingException(422, "The billing customer could not be created.");
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return MapRaw(raw, "The billing customer could not be created.");
        }

        return new BillingException(502, "The billing customer could not be created.");
    }

    private static BillingException MapCreateSubscription(SdkException<CreateSubscriptionError> ex)
    {
        if (ex.Error.TryGetErrorListResponse1(out var list) && list.Errors is { Count: > 0 })
        {
            return new BillingException(422, string.Join("; ", list.Errors));
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return MapRaw(raw, "The subscription could not be created.");
        }

        return new BillingException(422, "The subscription could not be created.");
    }

    private static BillingException MapReactivate(SdkException<ReactivateSubscriptionError> ex)
    {
        if (ex.Error.TryGetErrorListResponse1(out var list) && list.Errors is { Count: > 0 })
        {
            return new BillingException(422, string.Join("; ", list.Errors));
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return MapRaw(raw, "The subscription could not be reactivated.");
        }

        return new BillingException(422, "The subscription could not be reactivated.");
    }

    private static BillingException MapRaw(RawError raw, string fallback)
    {
        var status = (int)raw.StatusCode;
        if (status is >= 400 and < 500)
        {
            return new BillingException(status, fallback);
        }

        return new BillingException(status >= 500 ? status : 502, fallback);
    }

    private static SubscriptionPlan? MapPlan(Product product)
    {
        if (product.ArchivedAt is not null || string.IsNullOrWhiteSpace(product.Handle) || string.IsNullOrWhiteSpace(product.Name))
        {
            return null;
        }

        return new SubscriptionPlan(
            product.Handle,
            product.Name,
            product.Description,
            CentsToAmount(product.PriceInCents),
            product.Interval ?? 1,
            product.IntervalUnit?.Value ?? IntervalUnit.Month.Value);
    }

    private static ShopperSubscription MapSubscription(Subscription subscription)
    {
        return new ShopperSubscription(
            RequireId(subscription.Id),
            subscription.Product?.Handle,
            subscription.Product?.Name,
            CentsToAmount(subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents),
            subscription.State?.Value ?? "unknown",
            subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt);
    }

    private static bool IsOpen(Subscription subscription)
    {
        var state = subscription.State?.Value;
        if (string.IsNullOrWhiteSpace(state))
        {
            return true;
        }

        return state is not (
            "canceled" or "expired" or "trial_ended" or "failed_to_create");
    }

    private static bool CanReactivate(Subscription subscription)
    {
        var state = subscription.State?.Value;
        return state is "canceled" or "trial_ended" or "unpaid";
    }

    private static int RequireId(int? id)
    {
        if (id is int value)
        {
            return value;
        }

        throw new BillingException(502, "The billing provider returned a response that could not be processed.");
    }

    private static decimal CentsToAmount(long? cents) => (cents ?? 0) / 100m;

    private static string BuildSubscriptionReference(string userName, string productHandle) =>
        $"eshop:{userName}:{productHandle}";

    private static (string FirstName, string LastName, string Email) SplitIdentity(string userName)
    {
        var email = userName.Contains('@') ? userName : $"{userName}@eshop.local";
        var local = email.Split('@')[0];
        var first = string.IsNullOrWhiteSpace(local) ? "Shopper" : local;
        return (first, "eShop", email);
    }
}
