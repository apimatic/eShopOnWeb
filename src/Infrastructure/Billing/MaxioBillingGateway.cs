using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class MaxioBillingGateway : IMaxioBillingGateway
{
    private const int PageSize = 200;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;

    public MaxioBillingGateway(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ApiKey}:X"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var plans = new List<SubscriptionPlan>();
        for (var page = 1; ; page++)
        {
            var products = await GetAsync<List<ProductEnvelope>>(
                $"products.json?page={page}&per_page={PageSize}",
                cancellationToken) ?? new List<ProductEnvelope>();

            plans.AddRange(products
                .Select(envelope => envelope.Product)
                .Where(product =>
                    product.ArchivedAt is null &&
                    string.Equals(
                        product.ProductFamily?.Handle,
                        _options.ProductFamilyHandle,
                        StringComparison.OrdinalIgnoreCase))
                .Select(MapPlan));

            if (products.Count < PageSize)
            {
                break;
            }
        }

        return plans.OrderBy(plan => plan.PriceInCents).ThenBy(plan => plan.Name).ToArray();
    }

    public async Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        var envelope = await GetOrNullAsync<CustomerEnvelope>(
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}",
            cancellationToken);
        return envelope is null ? null : new MaxioCustomer(envelope.Customer.Id, envelope.Customer.Reference);
    }

    public async Task<MaxioCustomer> EnsureCustomerAsync(
        BillingUser user,
        string reference,
        CancellationToken cancellationToken)
    {
        var existing = await FindCustomerAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = CustomerNamesFromEmail(user.Email);
        using var response = await PostAsync(
            "customers.json",
            new CreateCustomerRequest(new CreateCustomer(firstName, lastName, user.Email, reference)),
            cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            var created = await DeserializeAsync<CustomerEnvelope>(response, cancellationToken);
            return new MaxioCustomer(created.Customer.Id, created.Customer.Reference);
        }

        // A concurrent caller can win the unique customer-reference race.
        existing = await FindCustomerWithRetryAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        throw await CreateProviderExceptionAsync(response, "Maxio rejected the customer enrollment.", cancellationToken);
    }

    public async Task<SubscriptionDetails> EnsureSubscriptionAsync(
        string productHandle,
        long customerId,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        var existing = await FindSubscriptionAsync(subscriptionReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        HttpResponseMessage response;
        try
        {
            response = await PostAsync(
                "subscriptions.json",
                new CreateSubscriptionRequest(new CreateSubscription(
                    productHandle,
                    customerId,
                    subscriptionReference)),
                cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            existing = await FindSubscriptionWithRetryAsync(subscriptionReference, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            throw new BillingProviderException("Maxio did not confirm the subscription before the request timed out.");
        }
        catch (HttpRequestException exception)
        {
            existing = await FindSubscriptionWithRetryAsync(subscriptionReference, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            throw new BillingProviderException("Maxio could not be reached to create the subscription.", exception);
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                return MapSubscription(
                    (await DeserializeAsync<SubscriptionEnvelope>(response, cancellationToken)).Subscription);
            }

            // Reconcile a duplicate-reference race or an ambiguous provider response.
            existing = await FindSubscriptionWithRetryAsync(subscriptionReference, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            throw await CreateProviderExceptionAsync(
                response,
                "Maxio rejected the subscription enrollment.",
                cancellationToken);
        }
    }

    public async Task<IReadOnlyList<SubscriptionDetails>> ListCustomerSubscriptionsAsync(
        long customerId,
        CancellationToken cancellationToken)
    {
        var allowedHandles = (await ListPlansAsync(cancellationToken))
            .Select(plan => plan.Handle)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var subscriptions = await GetAsync<List<SubscriptionEnvelope>>(
            $"customers/{customerId}/subscriptions.json",
            cancellationToken) ?? new List<SubscriptionEnvelope>();

        return subscriptions
            .Select(envelope => envelope.Subscription)
            .Where(subscription =>
                subscription.Product?.Handle is not null &&
                allowedHandles.Contains(subscription.Product.Handle))
            .Select(MapSubscription)
            .OrderByDescending(subscription => subscription.Id)
            .ToArray();
    }

    private async Task<SubscriptionDetails?> FindSubscriptionAsync(
        string reference,
        CancellationToken cancellationToken)
    {
        var envelope = await GetOrNullAsync<SubscriptionEnvelope>(
            $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}",
            cancellationToken);
        return envelope is null ? null : MapSubscription(envelope.Subscription);
    }

    private async Task<MaxioCustomer?> FindCustomerWithRetryAsync(
        string reference,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            var customer = await FindCustomerAsync(reference, cancellationToken);
            if (customer is not null || attempt == 4)
            {
                return customer;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken);
        }

        return null;
    }

    private async Task<SubscriptionDetails?> FindSubscriptionWithRetryAsync(
        string reference,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            var subscription = await FindSubscriptionAsync(reference, cancellationToken);
            if (subscription is not null || attempt == 4)
            {
                return subscription;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken);
        }

        return null;
    }

    private async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var response = await _httpClient.GetAsync(BuildUri(path), cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return await DeserializeAsync<T>(response, cancellationToken);
            }

            if (attempt < 3 && (response.StatusCode == HttpStatusCode.TooManyRequests ||
                                (int)response.StatusCode >= 500))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
                continue;
            }

            throw await CreateProviderExceptionAsync(response, "Maxio could not complete the billing request.", cancellationToken);
        }
    }

    private async Task<T?> GetOrNullAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(BuildUri(path), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw await CreateProviderExceptionAsync(response, "Maxio could not complete the billing lookup.", cancellationToken);
        }

        return await DeserializeAsync<T>(response, cancellationToken);
    }

    private Task<HttpResponseMessage> PostAsync<T>(string path, T value, CancellationToken cancellationToken) =>
        _httpClient.PostAsJsonAsync(BuildUri(path), value, JsonOptions, cancellationToken);

    private Uri BuildUri(string path) =>
        new($"{_options.ResolveBaseUrl().TrimEnd('/')}/{path.TrimStart('/')}", UriKind.Absolute);

    private static async Task<T> DeserializeAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        return value ?? throw new BillingProviderException("Maxio returned an empty or invalid response.");
    }

    private static async Task<BillingProviderException> CreateProviderExceptionAsync(
        HttpResponseMessage response,
        string message,
        CancellationToken cancellationToken)
    {
        var requestId = response.Headers.TryGetValues("X-Request-Id", out var values)
            ? values.FirstOrDefault()
            : null;
        await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var suffix = string.IsNullOrWhiteSpace(requestId) ? string.Empty : $" Reference: {requestId}.";
        return new BillingProviderException($"{message} (HTTP {(int)response.StatusCode}).{suffix}");
    }

    private static SubscriptionPlan MapPlan(MaxioProduct product) =>
        new(
            product.Id,
            product.Handle,
            product.Name,
            product.Description ?? string.Empty,
            product.PriceInCents,
            product.Interval,
            product.IntervalUnit,
            product.ProductPricePointName ?? "Default");

    private static SubscriptionDetails MapSubscription(MaxioSubscription subscription)
    {
        var product = subscription.Product ??
            throw new BillingProviderException("Maxio returned a subscription without a product.");
        return new SubscriptionDetails(
            subscription.Id,
            subscription.Reference ?? string.Empty,
            subscription.State,
            product.Handle,
            product.Name,
            subscription.ProductPriceInCents ?? product.PriceInCents,
            product.Interval,
            product.IntervalUnit,
            product.ProductPricePointName ?? "Default",
            subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt);
    }

    private static (string FirstName, string LastName) CustomerNamesFromEmail(string email)
    {
        var localPart = email.Split('@', 2)[0];
        var parts = localPart.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        var firstName = parts.Length > 0 ? parts[0] : "eShopOnWeb";
        var lastName = parts.Length > 1 ? string.Join(' ', parts.Skip(1)) : "Customer";
        return (firstName, lastName);
    }

    private sealed record ProductEnvelope([property: JsonPropertyName("product")] MaxioProduct Product);
    private sealed record CustomerEnvelope([property: JsonPropertyName("customer")] CustomerResponse Customer);
    private sealed record SubscriptionEnvelope([property: JsonPropertyName("subscription")] MaxioSubscription Subscription);
    private sealed record CreateCustomerRequest([property: JsonPropertyName("customer")] CreateCustomer Customer);
    private sealed record CreateSubscriptionRequest([property: JsonPropertyName("subscription")] CreateSubscription Subscription);

    private sealed record CreateCustomer(
        [property: JsonPropertyName("first_name")] string FirstName,
        [property: JsonPropertyName("last_name")] string LastName,
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("reference")] string Reference);

    private sealed record CreateSubscription(
        [property: JsonPropertyName("product_handle")] string ProductHandle,
        [property: JsonPropertyName("customer_id")] long CustomerId,
        [property: JsonPropertyName("reference")] string Reference,
        [property: JsonPropertyName("payment_collection_method")] string PaymentCollectionMethod = "remittance");

    private sealed record CustomerResponse(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("reference")] string Reference);

    private sealed record MaxioProduct(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("handle")] string Handle,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("price_in_cents")] long PriceInCents,
        [property: JsonPropertyName("interval")] int Interval,
        [property: JsonPropertyName("interval_unit")] string IntervalUnit,
        [property: JsonPropertyName("archived_at")] DateTimeOffset? ArchivedAt,
        [property: JsonPropertyName("product_price_point_name")] string? ProductPricePointName,
        [property: JsonPropertyName("product_family")] ProductFamily? ProductFamily);

    private sealed record ProductFamily([property: JsonPropertyName("handle")] string Handle);

    private sealed record MaxioSubscription(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("reference")] string? Reference,
        [property: JsonPropertyName("state")] string State,
        [property: JsonPropertyName("product_price_in_cents")] long? ProductPriceInCents,
        [property: JsonPropertyName("next_assessment_at")] DateTimeOffset? NextAssessmentAt,
        [property: JsonPropertyName("current_period_ends_at")] DateTimeOffset? CurrentPeriodEndsAt,
        [property: JsonPropertyName("product")] MaxioProduct? Product);
}
