using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Linq;
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
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Maxio Advanced Billing implementation of <see cref="IMaxioBillingService"/>.
///
/// Idempotency: the eShopOnWeb user id maps 1:1 to a Maxio customer reference
/// ("eshop-user-{userId}") — the provider enforces one customer per reference, so a
/// double-click create can never produce two customers. A subscription is keyed by a
/// deterministic reference ("eshop-user-{userId}-{planHandle}"): existing subscriptions are
/// found by reference before creating, and every rejection (422) is reconciled by reference
/// before surfacing an error, because the SDK retries transport failures on POST and a
/// retried signup may have succeeded.
/// </summary>
public sealed class MaxioBillingService : IMaxioBillingService
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private const int PageSize = 100;
    private const int MaxPages = 10;

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(MaxioAdvancedBillingClient client, MaxioOptions options, ILogger<MaxioBillingService> logger)
    {
        _client = client;
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanInfo>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var products = await ListAllProductsAsync(cancellationToken);
            return products
                .Where(p => p.Product is not null)
                .Select(p => MapPlan(p.Product!))
                .ToList();
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out var message))
            {
                // The 404 body is a plain string: the configured family handle does not exist.
                throw new MaxioBillingException("The configured subscription plan catalog was not found in Maxio.", 404, ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new MaxioBillingException(
                    DescribeRejection("listing subscription plans", raw), MapStatus(raw.StatusCode), ex);
            }
            throw new MaxioBillingException("Maxio returned an unexpected error while listing subscription plans.", null, ex);
        }
        catch (MaxioBillingException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw new MaxioBillingException(
                "Maxio Advanced Billing could not be reached or returned an unreadable response while listing subscription plans.", null, ex);
        }
    }

    public async Task<SubscriptionInfo> SubscribeAsync(string userId, string email, string planHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("A user id is required.", nameof(userId));
        if (string.IsNullOrWhiteSpace(email)) throw new ArgumentException("An email address is required.", nameof(email));
        if (string.IsNullOrWhiteSpace(planHandle)) throw new ArgumentException("A plan handle is required.", nameof(planHandle));

        try
        {
            var customerReference = CustomerReference(userId);
            var subscriptionReference = SubscriptionReference(userId, planHandle);

            var customer = await EnsureCustomerAsync(customerReference, email, cancellationToken);

            var existing = await FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation("User {UserId} is already subscribed to {PlanHandle} (Maxio subscription {SubscriptionId}).",
                    userId, planHandle, existing.Id);
                return MapSubscription(existing);
            }

            var response = await Bounded(ct => _client.Subscriptions.CreateSubscription(
                new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = planHandle,
                        CustomerId = customer.Id,
                        Reference = subscriptionReference,
                        // Manual collections: the seeded plans require no card capture, and an
                        // automatic-collection signup for an immediately-charged product is
                        // rejected with "No payment method was on file for the $... balance".
                        PaymentCollectionMethod = CollectionMethod.Remittance
                    }
                }, ct), cancellationToken);

            if (response.Subscription is null)
            {
                throw new MaxioBillingException("Maxio did not return the created subscription.", null);
            }

            _logger.LogInformation("User {UserId} subscribed to {PlanHandle} (Maxio subscription {SubscriptionId}).",
                userId, planHandle, response.Subscription.Id);
            return MapSubscription(response.Subscription);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            // A 422 here is either a real rejection (unknown plan) or a duplicate-reference
            // rejection from a concurrent/retried signup. Reconcile by reference first; only
            // report an error when no subscription actually exists.
            var reconciled = await ReconcileSubscriptionByReferenceAsync(
                SubscriptionReference(userId, planHandle), cancellationToken);
            if (reconciled is not null)
            {
                _logger.LogInformation("Subscription create for reference {Reference} was rejected, but the subscription already exists; returning it.",
                    SubscriptionReference(userId, planHandle));
                return MapSubscription(reconciled);
            }

            string detail;
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                detail = string.Join("; ", errorList.Errors);
            }
            else if (ex.Error.TryGetRawError(out var raw))
            {
                detail = DescribeRejection("creating the subscription", raw);
            }
            else
            {
                detail = "reason unavailable";
            }

            throw new MaxioBillingException($"Maxio rejected the subscription request: {detail}", 422, ex);
        }
        catch (MaxioBillingException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw new MaxioBillingException(
                "Maxio Advanced Billing could not be reached or returned an unreadable response while subscribing.", null, ex);
        }
    }

    public async Task<IReadOnlyList<SubscriptionInfo>> GetUserSubscriptionsAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("A user id is required.", nameof(userId));

        try
        {
            var customer = await TryReadCustomerByReferenceAsync(CustomerReference(userId), cancellationToken);
            if (customer?.Id is null)
            {
                return Array.Empty<SubscriptionInfo>();
            }

            var responses = await Bounded(ct => _client.Customers.ListCustomerSubscriptions(customer.Id.Value, ct), cancellationToken);
            return responses
                .Where(r => r.Subscription is not null)
                .Select(r => MapSubscription(r.Subscription!))
                .ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw new MaxioBillingException(
                DescribeRejection("listing the customer's subscriptions", ex.Error), MapStatus(ex.Error.StatusCode), ex);
        }
        catch (MaxioBillingException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw new MaxioBillingException(
                "Maxio Advanced Billing could not be reached or returned an unreadable response while listing subscriptions.", null, ex);
        }
    }

    // --- customer management ---------------------------------------------------

    private async Task<Customer> EnsureCustomerAsync(string customerReference, string email, CancellationToken cancellationToken)
    {
        var existing = await TryReadCustomerByReferenceAsync(customerReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            var response = await Bounded(ct => _client.Customers.CreateCustomer(
                new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = DeriveFirstName(email),
                        LastName = DeriveLastName(email),
                        Email = email,
                        Reference = customerReference
                    }
                }, ct), cancellationToken);

            return response.Customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // The provider enforces one customer per reference value: a 422 is almost always
            // a concurrent signup that won the race. Re-lookup and continue. (The generated
            // CustomerErrorResponse1 body model cannot carry real customer field errors, so
            // no structured detail is read from it.)
            var raced = await ReconcileCustomerByReferenceAsync(customerReference, cancellationToken);
            if (raced is not null)
            {
                _logger.LogInformation("Customer create for reference {Reference} hit a duplicate-reference rejection; using the existing customer {CustomerId}.",
                    customerReference, raced.Id);
                return raced;
            }

            string detail = ex.Error.TryGetRawError(out var raw)
                ? DescribeRejection("creating the customer", raw)
                : "validation failed";
            throw new MaxioBillingException($"Maxio rejected the customer create request: {detail}", 422, ex);
        }
        catch (JsonException ex)
        {
            // A non-2xx body that does not match the generated error model throws while the
            // error object is built. The request was still rejected: reconcile, then report
            // the rejection rather than an outage.
            var raced = await ReconcileCustomerByReferenceAsync(customerReference, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }
            throw new MaxioBillingException(
                "Maxio rejected the customer create request, but the rejection reason could not be read.", 422, ex);
        }
    }

    /// <summary>Reads the customer with the given reference; null only on a 404.</summary>
    private async Task<Customer?> TryReadCustomerByReferenceAsync(string customerReference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Bounded(ct => _client.Customers.ReadCustomerByReference(customerReference, ct), cancellationToken);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw new MaxioBillingException(
                DescribeRejection("looking up the customer", ex.Error), MapStatus(ex.Error.StatusCode), ex);
        }
    }

    // --- subscription lookups --------------------------------------------------

    /// <summary>Finds the subscription with the given reference; null only on a 404; throws <see cref="MaxioBillingException"/> on any other failure.</summary>
    private async Task<Subscription?> FindSubscriptionByReferenceAsync(string subscriptionReference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Bounded(ct => _client.Subscriptions.FindSubscription(subscriptionReference, ct), cancellationToken);
            return response.Subscription;
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                // 404: no subscription carries this reference.
                return null;
            }
            if (ex.Error.TryGetRawError(out var raw) && raw.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
            throw new MaxioBillingException(
                "Maxio returned an unexpected error while looking up the subscription by reference.", null, ex);
        }
    }

    /// <summary>Reconciliation lookup: like <see cref="FindSubscriptionByReferenceAsync"/> but treats any failure as "not found".</summary>
    private async Task<Subscription?> ReconcileSubscriptionByReferenceAsync(string subscriptionReference, CancellationToken cancellationToken)
    {
        try
        {
            return await FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
        }
        catch (MaxioBillingException)
        {
            return null;
        }
    }

    /// <summary>Reconciliation lookup: like <see cref="TryReadCustomerByReferenceAsync"/> but treats any failure as "not found".</summary>
    private async Task<Customer?> ReconcileCustomerByReferenceAsync(string customerReference, CancellationToken cancellationToken)
    {
        try
        {
            return await TryReadCustomerByReferenceAsync(customerReference, cancellationToken);
        }
        catch (MaxioBillingException)
        {
            return null;
        }
    }

    // --- helpers ---------------------------------------------------------------

    private async Task<IReadOnlyList<ProductResponse>> ListAllProductsAsync(CancellationToken cancellationToken)
    {
        var results = new List<ProductResponse>();
        var page = 1;
        IReadOnlyList<ProductResponse> batch;
        do
        {
            var currentPage = page;
            batch = await Bounded(ct => _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: $"handle:{_options.ProductFamilyHandle}",
                dateField: null,
                filter: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                includeArchived: null,
                include: null,
                page: currentPage,
                perPage: PageSize,
                ct: ct), cancellationToken);
            results.AddRange(batch);
            page++;
        }
        while (batch.Count == PageSize && page <= MaxPages);

        return results;
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private static SubscriptionPlanInfo MapPlan(Product product) => new()
    {
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? string.Empty,
        Description = product.Description,
        Price = (product.PriceInCents ?? 0) / 100m,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit?.Value,
        RequireCreditCard = product.RequireCreditCard ?? false
    };

    private static SubscriptionInfo MapSubscription(Subscription subscription) => new()
    {
        SubscriptionId = subscription.Id ?? 0,
        Reference = subscription.Reference,
        PlanHandle = subscription.Product?.Handle,
        PlanName = subscription.Product?.Name,
        State = subscription.State?.Value,
        Price = (subscription.ProductPriceInCents ?? 0) / 100m,
        NextBillingDate = subscription.CurrentPeriodEndsAt
    };

    private static string CustomerReference(string userId) => $"eshop-user-{userId}";

    private static string SubscriptionReference(string userId, string planHandle) =>
        $"eshop-user-{userId}-{planHandle}";

    private static int? MapStatus(HttpStatusCode statusCode) =>
        (int)statusCode is >= 400 and < 500 ? (int)statusCode : null;

    private static string DescribeRejection(string action, RawError raw) =>
        $"Maxio returned HTTP {(int)raw.StatusCode} while {action}.";

    private static string DeriveFirstName(string email)
    {
        var local = email.Split('@')[0];
        var first = local.Split(new[] { '.', '_', '-', '+' })[0];
        var name = Capitalize(first);
        return name.Length > 0 ? name : "eShop";
    }

    private static string DeriveLastName(string email)
    {
        var local = email.Split('@')[0];
        var parts = local.Split(new[] { '.', '_', '-', '+' });
        var name = Capitalize(parts.Length > 1 ? parts[^1] : "User");
        return name.Length > 0 ? name : "User";
    }

    private static string Capitalize(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value[1..];
}
