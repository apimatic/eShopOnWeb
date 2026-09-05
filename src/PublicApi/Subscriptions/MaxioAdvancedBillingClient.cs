using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// Server-side adapter for the verified Maxio Advanced Billing REST endpoints.
/// Maxio remains the subscription system of record; eShop only stores deterministic
/// references, so this works when the local in-memory database is restarted.
/// </summary>
public sealed class MaxioAdvancedBillingClient : IMaxioAdvancedBillingClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioAdvancedBillingClient> _logger;

    public MaxioAdvancedBillingClient(
        HttpClient httpClient,
        IOptions<MaxioOptions> options,
        ILogger<MaxioAdvancedBillingClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        ConfigureClient();
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var family = await GetRequiredAsync<MaxioProductFamilyResponse>(
            $"product_families/{EscapePathSegment($"handle:{_options.ProductFamilyHandle}")}.json",
            "reading the configured product family", cancellationToken);
        var familyId = family.ProductFamily?.Id ?? throw new MaxioApiException(HttpStatusCode.BadGateway, "reading the configured product family");

        var products = await GetRequiredAsync<List<MaxioProductResponse>>(
            $"product_families/{familyId}/products.json?per_page=200",
            "listing subscription plans", cancellationToken);

        return products
            .Select(x => x.Product)
            .Where(x => x is not null && x.ArchivedAt is null && !string.IsNullOrWhiteSpace(x.Handle))
            .Select(x => ToPlan(x!))
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<SubscriptionEnrollment> SubscribeAsync(MaxioCustomerInput customer, string planHandle, CancellationToken cancellationToken = default)
    {
        var plans = await GetPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(x => string.Equals(x.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new ArgumentException("The requested plan is not available in the configured Maxio product family.", nameof(planHandle));
        }

        var maxioCustomer = await EnsureCustomerAsync(customer, cancellationToken);
        var subscriptionReference = SubscriptionReference(customer.ApplicationUserId, plan.Handle);
        var existing = await FindSubscriptionAsync(subscriptionReference, cancellationToken);
        if (existing is not null)
        {
            return new SubscriptionEnrollment(ToSubscriptionDetails(existing, plan), false);
        }

        var request = new
        {
            subscription = new
            {
                product_handle = plan.Handle,
                customer_id = maxioCustomer.Id,
                reference = subscriptionReference,
                // The sandbox plans intentionally do not require card capture. Maxio's
                // documented remittance collection method creates the subscription
                // without a payment profile while keeping Maxio as the billing record.
                payment_collection_method = "remittance"
            }
        };

        try
        {
            var created = await PostRequiredAsync<MaxioSubscriptionResponse>("subscriptions.json", request,
                "creating the subscription", cancellationToken);
            var subscription = created.Subscription ?? throw new MaxioApiException(HttpStatusCode.BadGateway, "creating the subscription");
            return new SubscriptionEnrollment(ToSubscriptionDetails(subscription, plan), true);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // A concurrent retry can lose the create race. The deterministic reference
            // is the idempotency key; return the winner instead of creating another subscription.
            var winner = await FindSubscriptionAsync(subscriptionReference, cancellationToken);
            if (winner is not null)
            {
                return new SubscriptionEnrollment(ToSubscriptionDetails(winner, plan), false);
            }

            throw;
        }
    }

    public async Task<IReadOnlyList<SubscriptionDetails>> GetSubscriptionsAsync(MaxioCustomerInput customer, CancellationToken cancellationToken = default)
    {
        var maxioCustomer = await FindCustomerAsync(CustomerReference(customer.ApplicationUserId), cancellationToken);
        if (maxioCustomer is null)
        {
            return Array.Empty<SubscriptionDetails>();
        }

        var subscriptions = await GetRequiredAsync<List<MaxioSubscriptionResponse>>(
            $"customers/{maxioCustomer.Id}/subscriptions.json", "listing customer subscriptions", cancellationToken);

        return subscriptions
            .Select(x => x.Subscription)
            .Where(x => x is not null)
            .Select(x => ToSubscriptionDetails(x!, null))
            .OrderByDescending(x => x.Id)
            .ToArray();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(MaxioCustomerInput customer, CancellationToken cancellationToken)
    {
        var reference = CustomerReference(customer.ApplicationUserId);
        var existing = await FindCustomerAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var request = new
        {
            customer = new
            {
                first_name = customer.FirstName,
                last_name = customer.LastName,
                email = customer.Email,
                reference
            }
        };

        try
        {
            var created = await PostRequiredAsync<MaxioCustomerResponse>("customers.json", request,
                "creating the customer", cancellationToken);
            return created.Customer ?? throw new MaxioApiException(HttpStatusCode.BadGateway, "creating the customer");
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Maxio enforces customer-reference uniqueness. Re-read after a concurrent create.
            var winner = await FindCustomerAsync(reference, cancellationToken);
            if (winner is not null)
            {
                return winner;
            }

            throw;
        }
    }

    private async Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        var response = await GetOptionalAsync<MaxioCustomerResponse>(
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", "looking up the customer", cancellationToken);
        return response?.Customer;
    }

    private async Task<MaxioSubscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        var response = await GetOptionalAsync<MaxioSubscriptionResponse>(
            $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}", "looking up the subscription", cancellationToken);
        return response?.Subscription;
    }

    private static SubscriptionPlan ToPlan(MaxioProduct product) => new(
        product.Handle!, product.Name ?? product.Handle!, product.PriceInCents, product.PriceInCents / 100m,
        product.Interval.ToString(System.Globalization.CultureInfo.InvariantCulture), product.IntervalUnit ?? string.Empty, product.Currency);

    private static SubscriptionDetails ToSubscriptionDetails(MaxioSubscription subscription, SubscriptionPlan? catalogPlan)
    {
        var product = subscription.Product;
        var cents = subscription.ProductPriceInCents != 0 ? subscription.ProductPriceInCents : catalogPlan?.PriceInCents ?? product?.PriceInCents ?? 0;
        var handle = product?.Handle ?? catalogPlan?.Handle ?? "unknown";
        var name = product?.Name ?? catalogPlan?.Name ?? handle;
        return new SubscriptionDetails(subscription.Id, handle, name, cents, cents / 100m, product?.Currency ?? catalogPlan?.Currency,
            subscription.State ?? "unknown", subscription.CurrentPeriodEndsAt, subscription.NextAssessmentAt);
    }

    private async Task<T> GetRequiredAsync<T>(string requestUri, string operation, CancellationToken cancellationToken)
    {
        var result = await GetOptionalAsync<T>(requestUri, operation, cancellationToken);
        return result ?? throw new MaxioApiException(HttpStatusCode.BadGateway, operation);
    }

    private async Task<T?> GetOptionalAsync<T>(string requestUri, string operation, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(requestUri, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }

        return await ReadAsync<T>(response, operation, cancellationToken);
    }

    private async Task<T> PostRequiredAsync<T>(string requestUri, object body, string operation, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(requestUri, body, JsonOptions, cancellationToken);
        return await ReadAsync<T>(response, operation, cancellationToken);
    }

    private async Task<T> ReadAsync<T>(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Maxio request failed while {Operation} with HTTP {StatusCode}", operation, (int)response.StatusCode);
            throw new MaxioApiException(response.StatusCode, operation);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken)
            ?? throw new MaxioApiException(HttpStatusCode.BadGateway, operation);
    }

    private void ConfigureClient()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey) || string.IsNullOrWhiteSpace(_options.Subdomain) ||
            string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw new InvalidOperationException("Maxio configuration requires ApiKey, Subdomain, and ProductFamilyHandle.");
        }

        var baseUrl = string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? $"https://{_options.Subdomain}.chargify.com/"
            : _options.BaseUrl!;
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseAddress))
        {
            throw new InvalidOperationException("Maxio:BaseUrl must be an absolute URI when supplied.");
        }

        _httpClient.BaseAddress = baseAddress;
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var basicCredential = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ApiKey}:X"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basicCredential);
    }

    private static string CustomerReference(string userId) => $"eshop-user-{userId}";

    private static string SubscriptionReference(string userId, string planHandle)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{userId}\n{planHandle.ToLowerInvariant()}"));
        return $"eshop-sub-{Convert.ToHexString(bytes)[..32].ToLowerInvariant()}";
    }

    private static string EscapePathSegment(string value) => Uri.EscapeDataString(value);
}
