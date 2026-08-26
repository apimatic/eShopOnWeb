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
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// <see cref="ISubscriptionBillingService"/> over the Maxio Advanced Billing SDK.
/// Everything keys off handles (product family / product); numeric Maxio IDs are treated
/// as unstable. Idempotency: the eShopOnWeb username is stored as the Maxio customer's
/// unique <c>reference</c> (find-or-create), and subscribe pre-checks for an existing
/// active subscription to the same plan before creating one. A narrow concurrent
/// double-POST race remains — Maxio has no create-if-absent — and is accepted.
/// </summary>
public class MaxioBillingService : ISubscriptionBillingService
{
    public const string HttpClientName = "Maxio";

    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private const int PageSize = 100;

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

    public Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken = default)
        => Guarded("list subscription plans", async ct =>
        {
            var products = await ListFamilyProductsAsync(ct);
            return (IReadOnlyList<SubscriptionPlanDto>)products
                .Where(p => p.ArchivedAt is null)
                .Select(MapPlan)
                .ToList();
        }, cancellationToken);

    public Task<SubscribeResult> SubscribeAsync(string userReference, string userEmail, string productHandle, CancellationToken cancellationToken = default)
        => Guarded("create subscription", async ct =>
        {
            var plans = await ListFamilyProductsAsync(ct);
            var plan = plans.FirstOrDefault(p => p.ArchivedAt is null && p.Handle == productHandle);
            if (plan is null)
            {
                throw new BillingException(HttpStatusCode.BadRequest, $"Unknown subscription plan '{productHandle}'.");
            }

            var customer = await GetOrCreateCustomerAsync(userReference, userEmail, ct);
            if (customer.Id is null)
            {
                throw new BillingException(HttpStatusCode.BadGateway, "The billing provider returned an incomplete customer record.");
            }

            var existing = await FindActiveSubscriptionAsync(customer.Id.Value, productHandle, ct);
            if (existing is not null)
            {
                return new SubscribeResult { Subscription = MapSubscription(existing), Created = false };
            }

            var created = await _client.Subscriptions.CreateSubscription(
                new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = productHandle,
                        CustomerId = customer.Id,
                        Reference = $"{userReference}:{productHandle}",
                        // Remittance billing needs no card on file — the seeded plans capture no
                        // payment method, and the sandbox rejects automatic collection without one.
                        PaymentCollectionMethod = CollectionMethod.Remittance
                    }
                }, ct: ct);

            var subscription = created.Subscription
                ?? throw new BillingException(HttpStatusCode.BadGateway, "The billing provider returned an incomplete subscription.");
            return new SubscribeResult { Subscription = MapSubscription(subscription), Created = true };
        }, cancellationToken);

    public Task<IReadOnlyList<CustomerSubscriptionDto>> ListMySubscriptionsAsync(string userReference, CancellationToken cancellationToken = default)
        => Guarded("list subscriptions", async ct =>
        {
            Customer customer;
            try
            {
                var response = await _client.Customers.ReadCustomerByReference(userReference, ct: ct);
                customer = response.Customer;
            }
            catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                // No Maxio customer yet — the shopper has never subscribed. A genuine absence.
                return (IReadOnlyList<CustomerSubscriptionDto>)Array.Empty<CustomerSubscriptionDto>();
            }

            if (customer.Id is null)
            {
                throw new BillingException(HttpStatusCode.BadGateway, "The billing provider returned an incomplete customer record.");
            }

            var subscriptions = await _client.Customers.ListCustomerSubscriptions(customer.Id.Value, ct: ct);
            return (IReadOnlyList<CustomerSubscriptionDto>)subscriptions
                .Select(r => r.Subscription)
                .Where(s => s is not null)
                .Select(s => MapSubscription(s!))
                .ToList();
        }, cancellationToken);

    private async Task<List<Product>> ListFamilyProductsAsync(CancellationToken ct)
    {
        try
        {
            return await ListAllProductPagesAsync(page => _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: "handle:" + _options.ProductFamilyHandle,
                dateField: null,
                filter: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                includeArchived: null,
                include: null,
                page: page,
                perPage: PageSize,
                ct: ct));
        }
        catch (SdkException<ListProductsForProductFamilyError> ex) when (ex.Error.TryGetString(out _))
        {
            // 404 — the "handle:" family lookup is only documented for ReadProductFamily, so
            // fall back to a site-wide list filtered by the family handle.
            _logger.LogWarning("Maxio rejected the product-family handle lookup; falling back to a site-wide product list.");
            var all = await ListAllProductPagesAsync(page => _client.Products.ListProducts(
                dateField: null,
                filter: null,
                endDate: null,
                endDatetime: null,
                startDate: null,
                startDatetime: null,
                includeArchived: null,
                include: null,
                page: page,
                perPage: PageSize,
                ct: ct));
            return all.Where(p => p.ProductFamily?.Handle == _options.ProductFamilyHandle).ToList();
        }
    }

    private static async Task<List<Product>> ListAllProductPagesAsync(Func<int, Task<IReadOnlyList<ProductResponse>>> fetchPage)
    {
        var products = new List<Product>();
        var page = 1;
        while (true)
        {
            var responses = await fetchPage(page);
            products.AddRange(responses.Select(r => r.Product));
            if (responses.Count < PageSize)
            {
                break;
            }
            page++;
        }
        return products;
    }

    private async Task<Customer> GetOrCreateCustomerAsync(string reference, string email, CancellationToken ct)
    {
        try
        {
            var existing = await _client.Customers.ReadCustomerByReference(reference, ct: ct);
            return existing.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            // Genuine miss — create below.
        }

        var (firstName, lastName) = SplitName(email);
        try
        {
            var created = await _client.Customers.CreateCustomer(
                new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = firstName,
                        LastName = lastName,
                        Email = email,
                        Reference = reference
                    }
                }, ct: ct);
            return created.Customer;
        }
        catch (SdkException<CreateCustomerError> ex) when (ex.Error.TryGetCustomerErrorResponse1(out _))
        {
            // 422 — a concurrent create won the race on the unique reference; take the winner.
            var winner = await _client.Customers.ReadCustomerByReference(reference, ct: ct);
            return winner.Customer;
        }
    }

    private async Task<Subscription?> FindActiveSubscriptionAsync(int customerId, string productHandle, CancellationToken ct)
    {
        var subscriptions = await _client.Customers.ListCustomerSubscriptions(customerId, ct: ct);
        return subscriptions
            .Select(r => r.Subscription)
            .Where(s => s is not null)
            .FirstOrDefault(s => s!.Product?.Handle == productHandle && IsActiveish(s.State));
    }

    private static bool IsActiveish(SubscriptionState? state)
        => state == SubscriptionState.Active
        || state == SubscriptionState.Trialing
        || state == SubscriptionState.Assessing;

    // eShopOnWeb identity stores no names; Maxio requires them, so derive placeholders.
    private static (string FirstName, string LastName) SplitName(string email)
    {
        var local = email.Split('@')[0];
        return (string.IsNullOrWhiteSpace(local) ? "eShopOnWeb" : local, "Customer");
    }

    private static SubscriptionPlanDto MapPlan(Product product)
        => new()
        {
            Handle = product.Handle,
            Name = product.Name,
            PriceInCents = product.PriceInCents,
            Interval = product.Interval,
            IntervalUnit = product.IntervalUnit?.Value
        };

    private static CustomerSubscriptionDto MapSubscription(Subscription subscription)
        => new()
        {
            Id = subscription.Id,
            PlanHandle = subscription.Product?.Handle,
            PlanName = subscription.Product?.Name,
            PriceInCents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents,
            State = subscription.State?.Value,
            NextBillingDate = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt
        };

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey)
            || (string.IsNullOrWhiteSpace(_options.BaseUrl) && string.IsNullOrWhiteSpace(_options.Subdomain)))
        {
            throw new BillingException(HttpStatusCode.InternalServerError,
                "Maxio billing is not configured. Set Maxio:ApiKey and Maxio:Subdomain (or Maxio:BaseUrl) via user-secrets or environment variables.");
        }
    }

    /// <summary>
    /// The single integration boundary: bounds the whole call with a linked token and
    /// converts every SDK/transport failure into a <see cref="BillingException"/> with a
    /// caller-safe message and a deliberate status code.
    /// </summary>
    private async Task<T> Guarded<T>(string operation, Func<CancellationToken, Task<T>> body, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        try
        {
            return await body(cts.Token);
        }
        catch (BillingException)
        {
            throw;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            throw TranslateCreateCustomerError(operation, ex);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            throw TranslateCreateSubscriptionError(operation, ex);
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            throw TranslateListProductsError(operation, ex);
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRawError(operation, ex.Error);
        }
        catch (JsonException ex)
        {
            // Two opposite meanings share this type: a broken 2xx body (outcome unknown) and a
            // non-2xx body that didn't match the generated error shape (a rejection whose detail
            // was lost). The status-capture handler is the only place the status survives.
            var status = MaxioStatusCaptureHandler.LastStatus;
            _logger.LogWarning($"Maxio {operation}: response could not be processed (HTTP {(status.HasValue ? (int)status.Value : 0)}): {ex.Message}");
            if (status.HasValue && (int)status.Value >= 400 && (int)status.Value < 500)
            {
                throw new BillingException(status.Value, "The billing provider rejected the request.", ex);
            }
            throw new BillingException(HttpStatusCode.BadGateway, "The billing provider returned a response that could not be processed.", ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning($"Maxio {operation}: connection failure: {ex.Message}");
            throw new BillingException(HttpStatusCode.ServiceUnavailable, "The billing provider is unreachable.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new BillingException(HttpStatusCode.ServiceUnavailable, "The billing provider did not respond in time.", ex);
        }
    }

    private BillingException TranslateCreateCustomerError(string operation, SdkException<CreateCustomerError> ex)
    {
        if (ex.Error.TryGetCustomerErrorResponse1(out _))
        {
            // The generated 422 model does not carry customer field errors; the detail is logged
            // from the raw body where available, so the caller gets a safe generic message.
            _logger.LogWarning($"Maxio {operation}: customer rejected (422).");
            return new BillingException(HttpStatusCode.UnprocessableEntity, "The billing provider rejected the customer record.", ex);
        }
        if (ex.Error.TryGetRawError(out var raw))
        {
            return TranslateRawError(operation, raw);
        }
        return new BillingException(HttpStatusCode.BadGateway, "The billing provider returned an unexpected error.", ex);
    }

    private BillingException TranslateCreateSubscriptionError(string operation, SdkException<CreateSubscriptionError> ex)
    {
        if (ex.Error.TryGetErrorListResponse1(out var errorList))
        {
            var detail = string.Join("; ", errorList.Errors);
            _logger.LogWarning($"Maxio {operation}: subscription rejected (422): {detail}");
            return new BillingException(HttpStatusCode.UnprocessableEntity,
                $"The billing provider rejected the subscription: {detail}", ex);
        }
        if (ex.Error.TryGetRawError(out var raw))
        {
            return TranslateRawError(operation, raw);
        }
        return new BillingException(HttpStatusCode.BadGateway, "The billing provider returned an unexpected error.", ex);
    }

    private BillingException TranslateListProductsError(string operation, SdkException<ListProductsForProductFamilyError> ex)
    {
        if (ex.Error.TryGetString(out var message))
        {
            _logger.LogWarning($"Maxio {operation}: product family not found (404): {message}");
            return new BillingException(HttpStatusCode.NotFound, "The configured subscription plan catalog was not found.", ex);
        }
        if (ex.Error.TryGetRawError(out var raw))
        {
            return TranslateRawError(operation, raw);
        }
        return new BillingException(HttpStatusCode.BadGateway, "The billing provider returned an unexpected error.", ex);
    }

    private BillingException TranslateRawError(string operation, RawError raw)
    {
        var status = raw.StatusCode;
        string detail;
        try
        {
            detail = raw.ReadAsString();
        }
        catch (JsonException)
        {
            detail = "<unreadable body>";
        }
        _logger.LogWarning($"Maxio {operation} failed: HTTP {(int)status}: {detail}");

        if ((int)status >= 400 && (int)status < 500)
        {
            return new BillingException(status, $"The billing provider rejected the request (HTTP {(int)status}).");
        }
        return new BillingException(HttpStatusCode.BadGateway, "The billing provider returned an error.");
    }
}
