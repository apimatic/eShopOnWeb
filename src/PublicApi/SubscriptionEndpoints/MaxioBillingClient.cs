using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Single, documented integration boundary for the Maxio Billing API.</summary>
public interface IMaxioBillingClient
{
    Task<IReadOnlyList<MaxioPlan>> ListPlansAsync(CancellationToken cancellationToken);
    Task<MaxioPlan?> GetPlanAsync(string planHandle, CancellationToken cancellationToken);
    Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken);
    Task<MaxioCustomer> CreateCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken cancellationToken);
    Task<MaxioSubscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken);
    Task<MaxioSubscription> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken);
    Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle, string reference, CancellationToken cancellationToken);
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken);
}

public sealed class MaxioBillingException : Exception
{
    public MaxioBillingException(HttpStatusCode statusCode, string operation) : base($"Maxio {operation} failed with HTTP {(int)statusCode}.") => StatusCode = statusCode;
    public HttpStatusCode StatusCode { get; }
}

public sealed class MaxioBillingClient : IMaxioBillingClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioBillingClient> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public MaxioBillingClient(HttpClient httpClient, IOptions<MaxioOptions> options, ILogger<MaxioBillingClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MaxioPlan>> ListPlansAsync(CancellationToken cancellationToken)
    {
        _options.EnsureConfigured();
        var familyHandle = MaxioOptions.Required(_options.ProductFamilyHandle, nameof(MaxioOptions.ProductFamilyHandle));
        using var document = await SendAsync(HttpMethod.Get, $"product_families/handle:{Uri.EscapeDataString(familyHandle)}/products.json?per_page=200", null, cancellationToken);
        try
        {
            return Items(document.RootElement)
                .Select(item => ToPlan(Unwrap(item, "product")))
                .Where(plan => !plan.Archived)
                .OrderBy(plan => plan.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException or JsonException)
        {
            _logger.LogError(exception, "Maxio returned an unexpected product-family response shape.");
            throw;
        }
    }

    public async Task<MaxioPlan?> GetPlanAsync(string planHandle, CancellationToken cancellationToken) =>
        (await ListPlansAsync(cancellationToken)).SingleOrDefault(plan => string.Equals(plan.Handle, planHandle, StringComparison.Ordinal));

    public async Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        using var document = await SendOrNullOnNotFoundAsync(HttpMethod.Get, $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", null, cancellationToken);
        return document is null ? null : ToCustomer(document.RootElement.GetProperty("customer"));
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken cancellationToken)
    {
        using var document = await SendAsync(HttpMethod.Post, "customers.json", new
        {
            customer = new { first_name = firstName, last_name = lastName, email, reference }
        }, cancellationToken);
        return ToCustomer(document.RootElement.GetProperty("customer"));
    }

    public async Task<MaxioSubscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        using var document = await SendOrNullOnNotFoundAsync(HttpMethod.Get, $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}", null, cancellationToken);
        return document is null ? null : ToSubscription(document.RootElement.GetProperty("subscription"));
    }

    public async Task<MaxioSubscription> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken)
    {
        using var document = await SendAsync(HttpMethod.Get, $"subscriptions/{subscriptionId}.json", null, cancellationToken);
        return ToSubscription(document.RootElement.GetProperty("subscription"));
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle, string reference, CancellationToken cancellationToken)
    {
        using var document = await SendAsync(HttpMethod.Post, "subscriptions.json", new
        {
            // Invoice collection is the documented non-card collection mode for this card-free enrollment flow.
            subscription = new { customer_id = customerId, product_handle = productHandle, reference, payment_collection_method = "invoice" }
        }, cancellationToken);
        return ToSubscription(document.RootElement.GetProperty("subscription"));
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        using var document = await SendAsync(HttpMethod.Get, $"customers/{customerId}/subscriptions.json", null, cancellationToken);
        return Items(document.RootElement)
            .Select(item => ToSubscription(Unwrap(item, "subscription")))
            .ToArray();
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        var response = await SendRequestAsync(method, path, body, cancellationToken);
        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Maxio returned HTTP {StatusCode} for {Operation}.", (int)response.StatusCode, method.Method + " " + path.Split('?')[0]);
                throw new MaxioBillingException(response.StatusCode, method.Method + " " + path.Split('?')[0]);
            }
            return await response.Content.ReadFromJsonAsync<JsonDocument>(JsonOptions, cancellationToken) ?? throw new InvalidOperationException("Maxio returned an empty response.");
        }
    }

    private async Task<JsonDocument?> SendOrNullOnNotFoundAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        var response = await SendRequestAsync(method, path, body, cancellationToken);
        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Maxio returned HTTP {StatusCode} for {Operation}.", (int)response.StatusCode, method.Method + " " + path.Split('?')[0]);
                throw new MaxioBillingException(response.StatusCode, method.Method + " " + path.Split('?')[0]);
            }
            return await response.Content.ReadFromJsonAsync<JsonDocument>(JsonOptions, cancellationToken) ?? throw new InvalidOperationException("Maxio returned an empty response.");
        }
    }

    private Task<HttpResponseMessage> SendRequestAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        try { _options.EnsureConfigured(); }
        catch (MaxioConfigurationException exception)
        {
            _logger.LogError(exception, "Maxio configuration is invalid.");
            throw;
        }
        var request = new HttpRequestMessage(method, new Uri(_options.GetBaseUri(), path));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var credential = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{MaxioOptions.Required(_options.ApiKey, nameof(MaxioOptions.ApiKey))}:X"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credential);
        if (body is not null) request.Content = JsonContent.Create(body, options: JsonOptions);
        return _httpClient.SendAsync(request, cancellationToken);
    }

    private static MaxioPlan ToPlan(JsonElement product) => new(
        product.GetProperty("id").GetInt32(),
        product.GetProperty("handle").GetString() ?? throw new InvalidOperationException("Maxio product has no handle."),
        product.GetProperty("name").GetString() ?? string.Empty,
        product.TryGetProperty("description", out var description) ? description.GetString() : null,
        product.GetProperty("price_in_cents").GetInt64(),
        product.GetProperty("interval").GetInt32(),
        product.GetProperty("interval_unit").GetString() ?? string.Empty,
        product.TryGetProperty("archived_at", out var archivedAt) && archivedAt.ValueKind != JsonValueKind.Null);

    private static MaxioCustomer ToCustomer(JsonElement customer) => new(
        customer.GetProperty("id").GetInt32(), customer.GetProperty("reference").GetString() ?? string.Empty);

    private static MaxioSubscription ToSubscription(JsonElement subscription)
    {
        var product = subscription.TryGetProperty("product", out var productElement) && productElement.ValueKind != JsonValueKind.Null ? productElement : default;
        return new MaxioSubscription(
            subscription.GetProperty("id").GetInt32(),
            subscription.TryGetProperty("reference", out var reference) ? reference.GetString() : null,
            subscription.GetProperty("state").GetString() ?? string.Empty,
            product.ValueKind == JsonValueKind.Undefined ? null : product.GetProperty("handle").GetString(),
            product.ValueKind == JsonValueKind.Undefined ? null : product.GetProperty("name").GetString(),
            subscription.TryGetProperty("product_price_in_cents", out var price) && price.ValueKind != JsonValueKind.Null ? price.GetInt64() : null,
            ParseDate(subscription, "current_period_ends_at"),
            ParseDate(subscription, "next_assessment_at"));
    }

    private static DateTimeOffset? ParseDate(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(value.GetString(), out var parsed) ? parsed : null;

    private static JsonElement Unwrap(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var nested) ? nested : element;

    private static IEnumerable<JsonElement> Items(JsonElement element) =>
        (element.ValueKind == JsonValueKind.Array ? element : element.GetProperty("items")).EnumerateArray();

}

public sealed record MaxioPlan(int Id, string Handle, string Name, string? Description, long PriceInCents, int Interval, string IntervalUnit, bool Archived);
public sealed record MaxioCustomer(int Id, string Reference);
public sealed record MaxioSubscription(int Id, string? Reference, string State, string? PlanHandle, string? PlanName, long? PriceInCents, DateTimeOffset? CurrentPeriodEndsAt, DateTimeOffset? NextAssessmentAt);
