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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// HTTP implementation of <see cref="IMaxioApiClient"/>.
/// Talks to the Maxio Advanced Billing REST API over HTTPS using HTTP Basic authentication
/// (API key as username, "X" as password) as described in the Advanced Billing docs.
/// </summary>
public sealed class MaxioApiClient : IMaxioApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, IOptions<MaxioOptions> options, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        _httpClient.BaseAddress = new Uri(_options.ResolveBaseUrl());

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ApiKey}:X"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var envelope = await ReadAsync<CustomerEnvelope>(response, cancellationToken);
        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerCreateRequest request, CancellationToken cancellationToken)
    {
        var body = new CreateCustomerEnvelope { Customer = request };
        using var response = await _httpClient.PostAsJsonAsync("/customers.json", body, JsonOptions, cancellationToken);
        var envelope = await ReadAsync<CustomerEnvelope>(response, cancellationToken);
        return envelope!.Customer;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListFamilyProductsAsync(string productFamilyHandle, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"/product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json?per_page=200", cancellationToken);

        var envelopes = await ReadAsync<List<ProductEnvelope>>(response, cancellationToken) ?? new List<ProductEnvelope>();
        return envelopes
            .Where(e => e.Product is not null && e.Product.ArchivedAt is null)
            .Select(e => e.Product)
            .ToList();
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"/customers/{customerId}/subscriptions.json?per_page=200", cancellationToken);
        var envelopes = await ReadAsync<List<SubscriptionEnvelope>>(response, cancellationToken) ?? new List<SubscriptionEnvelope>();
        return envelopes.Select(e => e.Subscription).ToList();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioSubscriptionCreateRequest request, CancellationToken cancellationToken)
    {
        var body = new CreateSubscriptionEnvelope { Subscription = request };
        using var response = await _httpClient.PostAsJsonAsync("/subscriptions.json", body, JsonOptions, cancellationToken);
        var envelope = await ReadAsync<SubscriptionEnvelope>(response, cancellationToken);
        return envelope!.Subscription;
    }

    public async Task<MaxioSite?> GetSiteAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync("/site.json", cancellationToken);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return null;
            }

            var envelope = await ReadAsync<SiteEnvelope>(response, cancellationToken);
            return envelope?.Site;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to read site settings from Maxio; continuing without site profile.");
            return null;
        }
    }

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken) where T : class
    {
        if (response.IsSuccessStatusCode)
        {
            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
        }

        throw await BuildExceptionAsync(response, cancellationToken);
    }

    private static async Task<MaxioApiException> BuildExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string body;
        try
        {
            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch
        {
            body = string.Empty;
        }

        string message = TryExtractErrorMessage(body);
        if (string.IsNullOrWhiteSpace(message))
        {
            message = $"Maxio Advanced Billing API returned {(int)response.StatusCode} ({response.ReasonPhrase}).";
        }

        return new MaxioApiException(response.StatusCode, message);
    }

    private static string TryExtractErrorMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("errors", out var errors))
            {
                return FlattenErrors(errors);
            }
        }
        catch (JsonException)
        {
            // Not JSON; fall through and use the raw body.
        }

        return body.Length <= 500 ? body : body[..500];
    }

    private static string FlattenErrors(JsonElement errors)
    {
        if (errors.ValueKind == JsonValueKind.Array)
        {
            return string.Join(" ", errors.EnumerateArray().Select(e => e.GetString()).Where(s => !string.IsNullOrWhiteSpace(s)));
        }

        if (errors.ValueKind == JsonValueKind.Object)
        {
            var parts = new List<string>();
            foreach (var property in errors.EnumerateObject())
            {
                string value = FlattenErrors(property.Value);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    parts.Add(value);
                }
            }

            return string.Join(" ", parts);
        }

        return errors.GetString() ?? string.Empty;
    }
}
