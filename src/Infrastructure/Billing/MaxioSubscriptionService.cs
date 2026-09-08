using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Orchestrates the eShop "subscribe" capability against Maxio Advanced Billing, which acts as the
/// system of record for customers and subscriptions.
///
/// Identity &amp; idempotency model: the billing customer and each subscription carry a deterministic,
/// app-supplied <c>reference</c> derived from the stable eShop user id (and plan handle). Maxio enforces
/// that these references are unique, which makes a double-click (or two racing requests) collapse onto a
/// single customer and a single subscription: the loser of the race receives a "reference must be unique"
/// rejection, which this service resolves by reading back the winning record.
/// </summary>
internal sealed class MaxioSubscriptionService : ISubscriptionService{
    private const string ReferenceConflictMarker = "must be unique";
    private static readonly TimeSpan CatalogCacheLifetime = TimeSpan.FromMinutes(5);

    private readonly MaxioApiClient _apiClient;
    private readonly MaxioOptions _options;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        MaxioApiClient apiClient,
        IOptions<MaxioOptions> options,
        IMemoryCache cache,
        ILogger<MaxioSubscriptionService> logger)
    {
        _apiClient = apiClient;
        _options = options.Value;
        _cache = cache;
        _logger = logger;
        EnsureConfigured();
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey) ||
            string.IsNullOrWhiteSpace(_options.ProductFamilyHandle) ||
            (string.IsNullOrWhiteSpace(_options.BaseUrl) && string.IsNullOrWhiteSpace(_options.Subdomain)))
        {
            throw new InvalidOperationException(
                "Maxio is not configured. Set the Maxio configuration section keys Maxio:ApiKey, Maxio:Subdomain and " +
                "Maxio:ProductFamilyHandle (populated from MAXIO_API_KEY, MAXIO_SITE_SUBDOMAIN, MAXIO_DEFAULT_PRODUCT_FAMILY) " +
                "or provide an explicit Maxio:BaseUrl.");
        }
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var family = await ResolveProductFamilyAsync(cancellationToken);
        var products = await GetProductsForFamilyAsync(family.Id, cancellationToken);
        var currency = await GetSiteCurrencyAsync(cancellationToken);

        return products
            .Where(p => p.ArchivedAt == null)
            .Select(p => new SubscriptionPlan
            {
                Id = p.Id,
                Handle = p.Handle,
                Name = p.Name,
                Price = CentsToMajor(p.PriceInCents),
                Currency = currency,
                Interval = p.Interval ?? 1,
                IntervalUnit = string.IsNullOrWhiteSpace(p.IntervalUnit) ? "month" : p.IntervalUnit!,
                IsArchived = false
            })
            .OrderBy(p => p.Price)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<SubscriptionResult> SubscribeAsync(SubscriptionSignup signup, CancellationToken cancellationToken)
    {
        if (signup is null)
        {
            throw new ArgumentNullException(nameof(signup));
        }

        if (string.IsNullOrWhiteSpace(signup.UserId))
        {
            throw new ArgumentException("A user id is required to subscribe.", nameof(signup));
        }

        var family = await ResolveProductFamilyAsync(cancellationToken);
        var products = await GetProductsForFamilyAsync(family.Id, cancellationToken);
        var plan = products.FirstOrDefault(p =>
            p.ArchivedAt == null &&
            p.Handle.Equals(signup.PlanHandle, StringComparison.OrdinalIgnoreCase));

        if (plan is null)
        {
            throw new SubscriptionPlanNotFoundException(signup.PlanHandle);
        }

        var customerReference = BuildCustomerReference(signup.UserId);
        var customer = await EnsureCustomerAsync(customerReference, signup.Email, cancellationToken);

        var subscriptionReference = BuildSubscriptionReference(signup.UserId, family.Handle, plan.Handle);

        try
        {
            var created = await _apiClient.CreateSubscriptionAsync(customer.Id, plan.Handle, subscriptionReference, cancellationToken);
            _logger.LogInformation("Created Maxio subscription {SubscriptionId} for customer {CustomerId} on plan {PlanHandle} (reference {Reference}).",
                created.Id, customer.Id, plan.Handle, subscriptionReference);
            return new SubscriptionResult { Created = true, Subscription = MapSubscription(created) };
        }
        catch (MaxioApiException ex) when (IsReferenceConflict(ex))
        {
            // A concurrent request (double-click) won the race and created the subscription first.
            var existing = await _apiClient.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation("Subscription for plan {PlanHandle} (reference {Reference}) already exists as {SubscriptionId}; returning existing.",
                    plan.Handle, subscriptionReference, existing.Id);
                return new SubscriptionResult { Created = false, Subscription = MapSubscription(existing) };
            }

            throw new BillingGatewayException(ex.StatusCode, ex.Errors);
        }
        catch (MaxioApiException ex)
        {
            throw new BillingGatewayException(ex.StatusCode, ex.Errors);
        }
    }

    public async Task<IReadOnlyList<Subscription>> ListSubscriptionsAsync(string userId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Array.Empty<Subscription>();
        }

        var customerReference = BuildCustomerReference(userId);
        CustomerDto? customer;
        try
        {
            customer = await _apiClient.FindCustomerByReferenceAsync(customerReference, cancellationToken);
        }
        catch (MaxioApiException ex)
        {
            throw new BillingGatewayException(ex.StatusCode, ex.Errors);
        }

        if (customer is null)
        {
            return Array.Empty<Subscription>();
        }

        IReadOnlyList<SubscriptionDto> subscriptions;
        try
        {
            subscriptions = await _apiClient.ListSubscriptionsForCustomerAsync(customer.Id, cancellationToken);
        }
        catch (MaxioApiException ex)
        {
            throw new BillingGatewayException(ex.StatusCode, ex.Errors);
        }

        var family = await ResolveProductFamilyAsync(cancellationToken);
        return subscriptions
            .Where(s => s.Product?.ProductFamily?.Handle.Equals(family.Handle, StringComparison.OrdinalIgnoreCase) == true)
            .Select(MapSubscription)
            .OrderByDescending(s => s.CreatedAt)
            .ToList();
    }

    private async Task<CustomerDto> EnsureCustomerAsync(string customerReference, string email, CancellationToken cancellationToken)
    {
        var existing = await FindCustomerOrNullAsync(customerReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            var (firstName, lastName) = SplitName(email);
            var created = await _apiClient.CreateCustomerAsync(customerReference, email, firstName, lastName, cancellationToken);
            _logger.LogInformation("Created Maxio customer {CustomerId} for eShop user (reference {Reference}).", created.Id, customerReference);
            return created;
        }
        catch (MaxioApiException ex) when (IsReferenceConflict(ex))
        {
            // A concurrent request created the customer first.
            var winner = await FindCustomerOrNullAsync(customerReference, cancellationToken);
            if (winner is not null)
            {
                return winner;
            }

            throw new BillingGatewayException(ex.StatusCode, ex.Errors);
        }
        catch (MaxioApiException ex)
        {
            throw new BillingGatewayException(ex.StatusCode, ex.Errors);
        }
    }

    private async Task<CustomerDto?> FindCustomerOrNullAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            return await _apiClient.FindCustomerByReferenceAsync(reference, cancellationToken);
        }
        catch (MaxioApiException ex)
        {
            throw new BillingGatewayException(ex.StatusCode, ex.Errors);
        }
    }

    private static bool IsReferenceConflict(MaxioApiException ex) =>
        ex.StatusCode is >= 400 and < 500 &&
        ex.Errors.Any(e => e.Contains(ReferenceConflictMarker, StringComparison.OrdinalIgnoreCase));

    private static (string FirstName, string LastName) SplitName(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return ("eShop", "Customer");
        }

        var parts = email.Split(new[] { '@' }, 2);
        var firstName = parts[0];
        var lastName = parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[1]) ? parts[1] : "Customer";
        return (firstName, lastName);
    }

    private async Task<ProductFamilyDto> ResolveProductFamilyAsync(CancellationToken cancellationToken)
    {
        var key = $"maxio:family:{_options.ProductFamilyHandle.ToLowerInvariant()}";
        if (_cache.TryGetValue(key, out ProductFamilyDto? cached) && cached is not null)
        {
            return cached;
        }

        var family = await _apiClient.FindProductFamilyByHandleAsync(_options.ProductFamilyHandle, cancellationToken);
        if (family is null)
        {
            throw new InvalidOperationException(
                $"The configured Maxio product family '{_options.ProductFamilyHandle}' was not found on site '{_options.Subdomain}'. " +
                "Check the Maxio:ProductFamilyHandle (MAXIO_DEFAULT_PRODUCT_FAMILY) setting.");
        }

        _cache.Set(key, family, CatalogCacheLifetime);
        return family;
    }

    private async Task<IReadOnlyList<ProductDto>> GetProductsForFamilyAsync(int productFamilyId, CancellationToken cancellationToken)
    {
        var key = $"maxio:products:{productFamilyId}";
        if (_cache.TryGetValue(key, out List<ProductDto>? cached) && cached is not null)
        {
            return cached;
        }

        var products = (await _apiClient.ListProductsForFamilyAsync(productFamilyId, cancellationToken)).ToList();
        _cache.Set(key, products, CatalogCacheLifetime);
        return products;
    }

    private async Task<string> GetSiteCurrencyAsync(CancellationToken cancellationToken)
    {
        const string key = "maxio:site:currency";
        if (_cache.TryGetValue(key, out string? cached) && !string.IsNullOrWhiteSpace(cached))
        {
            return cached;
        }

        var site = await _apiClient.GetSiteAsync(cancellationToken);
        var currency = string.IsNullOrWhiteSpace(site.Currency) ? "USD" : site.Currency;
        _cache.Set(key, currency, CatalogCacheLifetime);
        return currency;
    }

    private string BuildCustomerReference(string userId) => $"eshop-u{SanitizeForReference(userId)}";

    private string BuildSubscriptionReference(string userId, string familyHandle, string planHandle) =>
        $"eshop-u{SanitizeForReference(userId)}-{familyHandle}-{planHandle}";

    private static string SanitizeForReference(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var ch in value.ToLowerInvariant())
        {
            if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '-')
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }

    private static Subscription MapSubscription(SubscriptionDto s)
    {
        var product = s.Product;
        return new Subscription
        {
            Id = s.Id,
            Reference = s.Reference ?? string.Empty,
            State = s.State,
            ProductId = product?.Id ?? 0,
            ProductHandle = product?.Handle ?? string.Empty,
            ProductName = product?.Name ?? string.Empty,
            Price = CentsToMajor(s.ProductPriceInCents ?? product?.PriceInCents),
            Currency = string.IsNullOrWhiteSpace(s.Currency) ? "USD" : s.Currency!,
            Interval = product?.Interval ?? 1,
            IntervalUnit = string.IsNullOrWhiteSpace(product?.IntervalUnit) ? "month" : product!.IntervalUnit!,
            PaymentCollectionMethod = s.PaymentCollectionMethod ?? string.Empty,
            CustomerId = s.Customer?.Id ?? 0,
            CreatedAt = s.CreatedAt ?? s.ActivatedAt ?? DateTimeOffset.MinValue,
            ActivatedAt = s.ActivatedAt,
            CurrentPeriodStartedAt = s.CurrentPeriodStartedAt,
            CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
            NextBillingDate = s.NextAssessmentAt ?? s.CurrentPeriodEndsAt
        };
    }

    private static decimal CentsToMajor(long? cents) => (cents ?? 0) / 100m;
}
