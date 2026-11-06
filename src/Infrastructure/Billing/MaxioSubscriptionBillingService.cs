using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Maxio Advanced Billing implementation of <see cref="ISubscriptionBillingService"/>.
/// The SDK has no idempotency key, so double-subscribe protection is: per-user lock, pre-check of the customer's
/// live subscriptions, and a re-check after any failed/ambiguous create (the SDK transport may resend a POST).
/// </summary>
public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private const int PAGE_SIZE = 200;

    private readonly Func<MaxioAdvancedBillingClient> _clientAccessor;
    private readonly string _familyHandle;
    private readonly Microsoft.Extensions.Logging.ILogger<MaxioSubscriptionBillingService> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);

    public MaxioSubscriptionBillingService(Func<MaxioAdvancedBillingClient> clientAccessor, string familyHandle,
        Microsoft.Extensions.Logging.ILogger<MaxioSubscriptionBillingService> logger)
    {
        _clientAccessor = clientAccessor;
        _familyHandle = familyHandle;
        _logger = logger;
    }

    private MaxioAdvancedBillingClient Client => _clientAccessor();

    // ---------------------------------------------------------------- public API

    public Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken ct = default) =>
        RunAsync(async () =>
        {
            var products = await ListFamilyProductsAsync(ct);
            IReadOnlyList<SubscriptionPlan> plans = products
                .Where(p => p.ArchivedAt == null && !string.IsNullOrEmpty(p.Handle))
                .Select(ToPlan)
                .ToList();
            return plans;
        }, ct);

    public Task<IReadOnlyList<UserSubscription>> ListSubscriptionsAsync(string userIdentity, CancellationToken ct = default) =>
        RunAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(userIdentity))
                throw new BillingException(HttpStatusCode.Unauthorized, "Unable to identify the user.");

            var customerId = await FindCustomerIdAsync(userIdentity, ct);
            if (customerId == null)
                return (IReadOnlyList<UserSubscription>)Array.Empty<UserSubscription>(); // never create a customer on a read path

            return await ListUserSubscriptionsAsync(customerId.Value, ct);
        }, ct);

    public Task<SubscribeResult> SubscribeAsync(string userIdentity, string planHandle, CancellationToken ct = default) =>
        RunAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(userIdentity))
                throw new BillingException(HttpStatusCode.Unauthorized, "Unable to identify the user.");
            if (string.IsNullOrWhiteSpace(planHandle))
                throw new BillingException(HttpStatusCode.BadRequest, "A plan handle is required.");

            var plan = await ReadPlanInFamilyAsync(planHandle.Trim(), ct);

            var gate = _locks.GetOrAdd(userIdentity, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(ct);
            try
            {
                var customerId = await EnsureCustomerAsync(userIdentity, ct);

                var existing = await FindLiveSubscriptionAsync(customerId, plan.Handle!, ct);
                if (existing != null)
                    return new SubscribeResult(existing, Created: false);

                var created = await CreateSubscriptionAsync(customerId, plan, ct);
                return new SubscribeResult(created, Created: true);
            }
            finally
            {
                gate.Release();
            }
        }, ct);

    // ---------------------------------------------------------------- plans

    private async Task<IReadOnlyList<Product>> ListFamilyProductsAsync(CancellationToken ct)
    {
        try
        {
            return await ListProductsPagedAsync("handle:" + _familyHandle, ct);
        }
        catch (Exception ex) when (ex is SdkException<ListProductsForProductFamilyError> or JsonException)
        {
            _logger.LogWarning("Listing products by family handle failed; resolving the family id instead.");
        }

        var families = await Client.ProductFamilies.ListProductFamilies(null, null, null, null, null, ct);
        var family = families
            .Select(f => f.ProductFamily)
            .FirstOrDefault(f => f?.Handle != null && string.Equals(f.Handle, _familyHandle, StringComparison.OrdinalIgnoreCase));
        if (family?.Id == null)
            throw new BillingException(HttpStatusCode.NotFound, "The configured product family was not found.");

        return await ListProductsPagedAsync(family.Id.Value.ToString(), ct);
    }

    private async Task<IReadOnlyList<Product>> ListProductsPagedAsync(string familyId, CancellationToken ct)
    {
        var all = new List<Product>();
        for (var page = 1; ; page++)
        {
            var batch = await Client.ProductFamilies.ListProductsForProductFamily(
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
                perPage: PAGE_SIZE,
                ct: ct);

            all.AddRange(batch.Select(b => b.Product).Where(p => p != null));
            if (batch.Count < PAGE_SIZE)
                return all;
        }
    }

    private async Task<Product> ReadPlanInFamilyAsync(string handle, CancellationToken ct)
    {
        Product product;
        try
        {
            product = (await Client.Products.ReadProductByHandle(handle, ct)).Product;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            throw new BillingException(HttpStatusCode.NotFound, $"Plan '{handle}' was not found.", ex);
        }

        var inFamily = string.Equals(product.ProductFamily?.Handle, _familyHandle, StringComparison.OrdinalIgnoreCase);
        if (!inFamily || product.ArchivedAt != null)
            throw new BillingException(HttpStatusCode.NotFound, $"Plan '{handle}' was not found.");

        return product;
    }

    private static SubscriptionPlan ToPlan(Product p) => new(
        p.Handle!,
        p.Name ?? p.Handle!,
        p.Description,
        (p.PriceInCents ?? 0) / 100m,
        p.Interval,
        p.IntervalUnit?.Value,
        p.RequireCreditCard ?? false);

    // ---------------------------------------------------------------- customers

    private async Task<int?> FindCustomerIdAsync(string reference, CancellationToken ct)
    {
        try
        {
            return (await Client.Customers.ReadCustomerByReference(reference, ct)).Customer.Id;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<int> EnsureCustomerAsync(string reference, CancellationToken ct)
    {
        var id = await FindCustomerIdAsync(reference, ct);
        if (id != null)
            return id.Value;

        var (first, last) = DeriveNames(reference);
        var request = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = first,
                LastName = last,
                Email = reference,
                Reference = reference,
            },
        };

        try
        {
            var response = await Client.Customers.CreateCustomer(request, ct);
            if (response.Customer.Id is { } createdId)
                return createdId;
        }
        catch (Exception ex) when (ex is SdkException<CreateCustomerError> or JsonException or HttpRequestException)
        {
            // Race with another request, an unreadable error body, or a resent POST: settle it by re-reading.
            _logger.LogWarning("CreateCustomer did not complete cleanly; re-reading customer by reference.");
        }

        id = await FindCustomerIdAsync(reference, ct);
        return id ?? throw new BillingException(HttpStatusCode.UnprocessableEntity,
            "The billing customer could not be created for this account.");
    }

    private static (string First, string Last) DeriveNames(string identity)
    {
        var local = identity.Split('@')[0].Trim();
        return (string.IsNullOrEmpty(local) ? "Shopper" : local, "Shopper");
    }

    // ---------------------------------------------------------------- subscriptions

    private async Task<IReadOnlyList<UserSubscription>> ListUserSubscriptionsAsync(int customerId, CancellationToken ct)
    {
        var response = await Client.Customers.ListCustomerSubscriptions(customerId, ct);
        return response
            .Select(r => r.Subscription)
            .Where(s => s != null)
            .Select(s => ToUserSubscription(s!))
            .ToList();
    }

    private async Task<UserSubscription?> FindLiveSubscriptionAsync(int customerId, string planHandle, CancellationToken ct)
    {
        var response = await Client.Customers.ListCustomerSubscriptions(customerId, ct);
        return response
            .Select(r => r.Subscription)
            .Where(s => s != null && IsLive(s) &&
                        string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase))
            .Select(s => ToUserSubscription(s!))
            .FirstOrDefault();
    }

    private static bool IsLive(Subscription s) =>
        s.State != SubscriptionState.Canceled &&
        s.State != SubscriptionState.Expired &&
        s.State != SubscriptionState.FailedToCreate;

    private async Task<UserSubscription> CreateSubscriptionAsync(int customerId, Product plan, CancellationToken ct)
    {
        var request = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = plan.Handle,
                CustomerId = customerId,
                // Plans do not require a card; bill by remittance so no payment method is needed at signup.
                PaymentCollectionMethod = CollectionMethod.Remittance,
            },
        };

        try
        {
            var response = await Client.Subscriptions.CreateSubscription(request, ct);
            if (response.Subscription != null)
                return ToUserSubscription(response.Subscription, plan);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var list))
                throw new BillingException(HttpStatusCode.UnprocessableEntity,
                    "The subscription was rejected: " + string.Join("; ", list.Errors), ex);
            if (ex.Error.TryGetRawError(out var raw))
                throw ToBillingException(raw.StatusCode, ex);
            throw new BillingException(HttpStatusCode.BadGateway, "The billing provider rejected the request.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            // Outcome unknown (the POST may have been applied, possibly more than once): reconcile below.
            _logger.LogWarning("CreateSubscription outcome unknown; re-reading customer subscriptions.");
        }

        var reconciled = await FindLiveSubscriptionAsync(customerId, plan.Handle!, ct);
        return reconciled ?? throw new BillingException(HttpStatusCode.BadGateway,
            "The billing provider returned an unreadable response; the subscription was not confirmed.");
    }

    private static UserSubscription ToUserSubscription(Subscription s, Product? fallbackPlan = null)
    {
        var product = s.Product ?? fallbackPlan;
        var cents = s.ProductPriceInCents ?? product?.PriceInCents ?? 0;
        return new UserSubscription(
            s.Id ?? 0,
            product?.Handle ?? string.Empty,
            product?.Name ?? product?.Handle ?? string.Empty,
            cents / 100m,
            s.State?.Value ?? "unknown",
            s.NextAssessmentAt ?? s.CurrentPeriodEndsAt);
    }

    // ---------------------------------------------------------------- error boundary

    /// <summary>Translates every SDK/transport failure into <see cref="BillingException"/> with a caller-safe message.</summary>
    private async Task<T> RunAsync<T>(Func<Task<T>> action, CancellationToken ct)
    {
        try
        {
            return await action();
        }
        catch (BillingException)
        {
            throw;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("Maxio:", StringComparison.Ordinal))
        {
            throw new BillingException(HttpStatusCode.ServiceUnavailable, "Subscription billing is not configured.", ex);
        }
        catch (SdkException<RawError> ex)
        {
            throw ToBillingException(ex.Error.StatusCode, ex);
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            throw new BillingException(HttpStatusCode.BadGateway, "The billing provider rejected the request.", ex);
        }
        catch (JsonException ex)
        {
            throw new BillingException(HttpStatusCode.BadGateway,
                "The billing provider returned a response that could not be processed.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            throw new BillingException(HttpStatusCode.GatewayTimeout, "The billing provider is unreachable.", ex);
        }
    }

    /// <summary>Provider 400/404/409/422 are caller-actionable and pass through; anything else (401, 5xx...) is an upstream fault.</summary>
    private static BillingException ToBillingException(HttpStatusCode status, Exception inner) => status switch
    {
        HttpStatusCode.BadRequest or HttpStatusCode.NotFound or HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity
            => new BillingException(status, "The billing provider rejected the request.", inner),
        _ => new BillingException(HttpStatusCode.BadGateway, "The billing provider is unavailable.", inner),
    };
}
