using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mb = MaxioAdvancedBilling.Models;
using MbEnums = MaxioAdvancedBilling.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Maxio Advanced Billing implementation of <see cref="IMaxioBillingService"/>.
///
/// Idempotency contract (Maxio is the system of record; nothing is persisted
/// locally):
/// - the Maxio customer reference is <c>eshop-{userId}</c> — Maxio enforces
///   reference uniqueness server-side, and a duplicate create is resolved by
///   re-reading by reference;
/// - the Maxio subscription reference is <c>eshop-{userId}-{planHandle}</c> —
///   a deterministic per-user-per-plan key, probed before create and re-probed
///   after any ambiguous create outcome.
/// </summary>
public sealed class MaxioBillingService : IMaxioBillingService
{
    // Maxio Basic auth: username = API key, password = the literal "x".
    private const string BasicAuthPassword = "x";

    private const string CustomerNameFallbackFirst = "eShop";
    private const string CustomerNameFallbackLast = "Shopper";

    // Total bound for one Maxio call. The per-attempt bounds live on the named
    // HttpClient and the SDK retry options; this is the only whole-call ceiling.
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    private const int ProductsPerPage = 50;

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(
        IHttpClientFactory httpClientFactory,
        IOptions<MaxioOptions> options,
        ILogger<MaxioBillingService> logger)
    {
        _options = options.Value;
        _options.Validate();
        _logger = logger;

        var httpClient = httpClientFactory.CreateClient(MaxioServiceCollectionExtensions.HttpClientName);
        var clientOptions = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials { Username = _options.ApiKey, Password = BasicAuthPassword },
            Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(10) }
        };
        // {site} in the base-URL template defaults to the literal string
        // "subdomain" — it must always be set to the real subdomain.
        clientOptions.Server.Production.Us.Site = _options.Subdomain;
        if (!string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            clientOptions.Server.Production.Us.BaseUrl = _options.BaseUrl;
        }

        _client = new MaxioAdvancedBillingClient(httpClient, clientOptions);
    }

    public async Task<IReadOnlyList<SubscriptionPlanInfo>> ListPlansAsync(CancellationToken ct)
    {
        var family = await ResolveFamilyAsync(ct);
        var products = await ListFamilyProductsAsync(family, ct);

        return products
            .Where(p => p.Product is { Handle: not null } && p.Product.ArchivedAt == null)
            .Select(p => MapPlan(p.Product!))
            .ToList();
    }

    public async Task<SubscriptionInfo> SubscribeAsync(string userId, string email, string planHandle, CancellationToken ct)
    {
        var plan = (await ListPlansAsync(ct))
            .FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase))
            ?? throw new MaxioBillingException(MaxioBillingError.PlanNotFound, $"Plan '{planHandle}' is not available.");

        var customer = await EnsureCustomerAsync(userId, email, ct);
        var subscriptionReference = SubscriptionReference(userId, plan.Handle);

        var existing = await FindSubscriptionOrNullAsync(subscriptionReference, ct);
        if (existing != null)
        {
            _logger.LogInformation("User {UserId} is already subscribed to plan {PlanHandle} (subscription {SubscriptionId}).",
                userId, plan.Handle, existing.Id);
            return MapSubscription(existing);
        }

        var body = new Mb.CreateSubscriptionRequest
        {
            Subscription = new Mb.CreateSubscription
            {
                ProductHandle = plan.Handle,
                CustomerId = customer.Id,
                Reference = subscriptionReference,
                // The sandbox plans are subscribed without card capture; remittance
                // collection lets the signup complete with no payment profile on file.
                PaymentCollectionMethod = MbEnums.CollectionMethod.Remittance
            }
        };

        string? rejectionDetail = null;
        bool outcomeUnknown = false;
        Mb.Subscription? created = null;

        try
        {
            var response = await Bounded(token => _client.Subscriptions.CreateSubscription(body, token), ct);
            created = response.Subscription;
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            // One branch per TryGet* on CreateSubscriptionError — 422 first, raw fallback last.
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                rejectionDetail = string.Join("; ", errorList.Errors ?? Array.Empty<string>());
                _logger.LogWarning("Maxio rejected subscription create for user {UserId}, plan {PlanHandle}: {Detail}",
                    userId, plan.Handle, rejectionDetail);
            }
            else if (ex.Error.TryGetRawError(out var raw))
            {
                rejectionDetail = DescribeRaw(raw);
                _logger.LogWarning("Maxio rejected subscription create for user {UserId}, plan {PlanHandle} with HTTP {Status}",
                    userId, plan.Handle, (int)raw.StatusCode);
            }
        }
        catch (MaxioBillingException ex) when (ex.Error == MaxioBillingError.UnreadableResponse)
        {
            // A response that could not be parsed — the create may or may not
            // have taken effect. Settle it by re-reading provider state.
            outcomeUnknown = true;
        }

        if (created == null)
        {
            // Ambiguous or rejected create: re-probe by reference — a concurrent
            // duplicate (or a success whose response was unreadable) is resolved
            // to the winner.
            var winner = await FindSubscriptionOrNullAsync(subscriptionReference, ct);
            if (winner != null)
            {
                _logger.LogInformation("Subscription {SubscriptionId} already exists for user {UserId}, plan {PlanHandle}; returning it.",
                    winner.Id, userId, plan.Handle);
                return MapSubscription(winner);
            }

            if (rejectionDetail != null)
            {
                throw new MaxioBillingException(MaxioBillingError.ProviderRejected,
                    $"Maxio rejected the subscription: {rejectionDetail}");
            }
            if (outcomeUnknown)
            {
                throw new MaxioBillingException(MaxioBillingError.UnreadableResponse,
                    "The subscription outcome could not be determined; retry the request.");
            }
            throw new MaxioBillingException(MaxioBillingError.ProviderError,
                "Maxio failed to create the subscription.");
        }

        return MapSubscription(created);
    }

    public async Task<IReadOnlyList<SubscriptionInfo>> ListUserSubscriptionsAsync(string userId, CancellationToken ct)
    {
        var customer = await ReadCustomerByReferenceOrNullAsync(CustomerReference(userId), ct);
        if (customer == null)
        {
            return Array.Empty<SubscriptionInfo>();
        }

        var customerId = customer.Id
            ?? throw new MaxioBillingException(MaxioBillingError.UnreadableResponse, "Maxio returned a customer without an id.");

        var responses = await Bounded(token => _client.Customers.ListCustomerSubscriptions(customerId, token), ct);
        return responses
            .Where(r => r.Subscription != null)
            .Select(r => MapSubscription(r.Subscription!))
            .ToList();
    }

    private async Task<Mb.ProductFamily> ResolveFamilyAsync(CancellationToken ct)
    {
        var families = await Bounded(
            token => _client.ProductFamilies.ListProductFamilies(
                dateField: null, startDate: null, endDate: null, startDatetime: null, endDatetime: null, ct: token),
            ct);

        var family = families
            .Select(f => f.ProductFamily)
            .FirstOrDefault(pf => pf != null && string.Equals(pf.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase));

        if (family == null)
        {
            throw new MaxioBillingException(MaxioBillingError.FamilyNotFound,
                $"Product family '{_options.ProductFamilyHandle}' was not found in Maxio.");
        }
        return family;
    }

    private async Task<List<Mb.ProductResponse>> ListFamilyProductsAsync(Mb.ProductFamily family, CancellationToken ct)
    {
        var familyId = family.Id?.ToString(CultureInfo.InvariantCulture)
            ?? throw new MaxioBillingException(MaxioBillingError.UnreadableResponse, "Maxio returned a product family without an id.");

        var products = new List<Mb.ProductResponse>();
        var page = 1;
        while (true)
        {
            var batch = await Bounded(
                token => _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: familyId,
                    dateField: null,
                    filter: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    includeArchived: null,
                    include: null,
                    page: page,
                    perPage: ProductsPerPage,
                    ct: token),
                ct);
            products.AddRange(batch);
            if (batch.Count < ProductsPerPage)
            {
                return products;
            }
            page++;
        }
    }

    /// <summary>
    /// Returns the Maxio customer for <paramref name="userId"/>, creating it on
    /// first use. Safe under concurrency: Maxio enforces customer reference
    /// uniqueness, and any duplicate-create race is settled by re-reading.
    /// </summary>
    private async Task<Mb.Customer> EnsureCustomerAsync(string userId, string email, CancellationToken ct)
    {
        var reference = CustomerReference(userId);

        var existing = await ReadCustomerByReferenceOrNullAsync(reference, ct);
        if (existing != null)
        {
            return existing;
        }

        var body = new Mb.CreateCustomerRequest
        {
            Customer = new Mb.CreateCustomer
            {
                FirstName = CustomerNameFallbackFirst,
                LastName = CustomerNameFallbackLast,
                Email = email,
                Reference = reference
            }
        };

        string? rejectionDetail = null;
        bool outcomeUnknown = false;
        Mb.Customer? created = null;

        try
        {
            var response = await Bounded(token => _client.Customers.CreateCustomer(body, token), ct);
            created = response.Customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // One branch per TryGet* on CreateCustomerError — 422 first, raw fallback last.
            // The typed 422 shape models only unrelated keys, so the duplicate-
            // reference message is not representable; either way, settle by re-read.
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                _logger.LogWarning("Maxio returned 422 on customer create for reference {Reference}.", reference);
            }
            else if (ex.Error.TryGetRawError(out var raw))
            {
                rejectionDetail = DescribeRaw(raw);
                _logger.LogWarning("Maxio rejected customer create for reference {Reference} with HTTP {Status}",
                    reference, (int)raw.StatusCode);
            }
        }
        catch (MaxioBillingException ex) when (ex.Error == MaxioBillingError.UnreadableResponse)
        {
            // An error body that could not be parsed — the create may or may
            // not have taken effect. Settle it by re-reading provider state.
            outcomeUnknown = true;
        }

        if (created == null)
        {
            var winner = await ReadCustomerByReferenceOrNullAsync(reference, ct);
            if (winner != null)
            {
                _logger.LogInformation("Customer {CustomerId} already exists for reference {Reference}; returning it.",
                    winner.Id, reference);
                return winner;
            }

            if (rejectionDetail != null)
            {
                throw new MaxioBillingException(MaxioBillingError.ProviderRejected,
                    $"Maxio rejected the customer: {rejectionDetail}");
            }
            if (outcomeUnknown)
            {
                throw new MaxioBillingException(MaxioBillingError.UnreadableResponse,
                    "The customer outcome could not be determined; retry the request.");
            }
            throw new MaxioBillingException(MaxioBillingError.ProviderError,
                "Maxio failed to create the customer.");
        }

        return created;
    }

    private async Task<Mb.Customer?> ReadCustomerByReferenceOrNullAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await Bounded(token => _client.Customers.ReadCustomerByReference(reference, token), ct);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<Mb.Subscription?> FindSubscriptionOrNullAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await Bounded(token => _client.Subscriptions.FindSubscription(reference, token), ct);
            return response.Subscription;
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            // One branch per TryGet* on FindSubscriptionError — 404 first, raw fallback last.
            if (ex.Error.TryGetNoContent(out _))
            {
                return null;
            }
            if (ex.Error.TryGetRawError(out var raw) && raw.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
            throw;
        }
    }

    /// <summary>
    /// Bounds one Maxio call end to end: links the caller's cancellation with a
    /// total budget, and converts transport failures and unreadable bodies into
    /// <see cref="MaxioBillingException"/> so callers face a single failure type.
    /// </summary>
    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        try
        {
            return await call(cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("A Maxio call exceeded the {Budget}s total budget.", CallBudget.TotalSeconds);
            throw new MaxioBillingException(MaxioBillingError.ProviderUnreachable, "Maxio did not answer in time.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Maxio could not be reached.");
            throw new MaxioBillingException(MaxioBillingError.ProviderUnreachable, "Maxio could not be reached.", ex);
        }
        catch (JsonException ex)
        {
            // Covers both hazards: a 2xx body that no longer matches the model,
            // and a non-2xx body that fails to parse while the SDK builds its
            // error object (the status is lost with it). Callers treat the
            // outcome as unknown and settle it by re-reading provider state.
            _logger.LogWarning(ex, "Maxio returned a response that could not be processed.");
            throw new MaxioBillingException(MaxioBillingError.UnreadableResponse,
                "Maxio returned a response that could not be processed.", ex);
        }
    }

    private static string DescribeRaw(RawError raw)
    {
        var body = raw.ReadAsString();
        return $"HTTP {(int)raw.StatusCode}" + (string.IsNullOrEmpty(body) ? "" : $": {body}");
    }

    internal static string CustomerReference(string userId) => $"eshop-{userId}";

    internal static string SubscriptionReference(string userId, string planHandle) =>
        $"{CustomerReference(userId)}-{planHandle}";

    private static SubscriptionPlanInfo MapPlan(Mb.Product product) => new()
    {
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? string.Empty,
        Description = product.Description,
        PriceInCents = product.PriceInCents ?? 0,
        Price = (product.PriceInCents ?? 0) / 100m,
        Interval = product.Interval ?? 0,
        IntervalUnit = product.IntervalUnit?.Value ?? string.Empty,
        RequireCreditCard = product.RequireCreditCard ?? false
    };

    private static SubscriptionInfo MapSubscription(Mb.Subscription subscription) => new()
    {
        Id = subscription.Id ?? 0,
        Reference = subscription.Reference,
        PlanHandle = subscription.Product?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? string.Empty,
        Price = (subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? 0) / 100m,
        State = subscription.State?.Value ?? string.Empty,
        NextBillingDate = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        Balance = (subscription.BalanceInCents ?? 0) / 100m
    };
}
