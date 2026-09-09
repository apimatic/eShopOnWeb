using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

// ---- wire models (serialized snake_case to match the Billing API) ----

public class MaxioCustomer
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Reference { get; set; }
}

public class MaxioProduct
{
    public int Id { get; set; }
    public string? Handle { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = "month";
    public DateTime? ArchivedAt { get; set; }
    public bool RequireCreditCard { get; set; }
}

public class MaxioSubscription
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public long? ProductPriceInCents { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public MaxioCustomer? Customer { get; set; }
    public MaxioProduct? Product { get; set; }
}

/// <summary>
/// Thin typed client over the Maxio Advanced Billing REST API (Billing API).
/// Authentication is HTTP Basic: the site API key as username, "x" as password,
/// over TLS, against https://{subdomain}.chargify.com (US/EU environments).
/// </summary>
public class MaxioClient
{
    private const int GetMaxAttempts = 3;

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public MaxioClient(HttpClient httpClient, Microsoft.Extensions.Options.IOptions<MaxioOptions> options)
    {
        _options = options.Value;
        _httpClient = httpClient;
        _httpClient.BaseAddress ??= new Uri(_options.EffectiveBaseUrl + "/");
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <summary>
    /// Lists the non-archived products of the configured product family
    /// (these are the subscription plans shown to shoppers).
    /// </summary>
    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get,
            $"product_families/handle:{Uri.EscapeDataString(_options.ProductFamilyHandle)}/products.json?per_page=200",
            retry: true, cancellationToken: cancellationToken);
        await EnsureSuccessAsync(response);

        var products = await ParseListAsync<MaxioProduct>(response, "product", cancellationToken);

        return products
            .Where(p => p.ArchivedAt is null)
            .ToList();
    }

    /// <summary>
    /// Returns the customer whose app-side reference matches, or null when none exists.
    /// </summary>
    public async Task<MaxioCustomer?> LookupCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get,
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}",
            retry: true, cancellationToken: cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        await EnsureSuccessAsync(response);

        var envelope = await DeserializeAsync<MaxioEnvelope<MaxioCustomer>>(response, cancellationToken);
        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(string reference, string firstName, string lastName, string email, CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            customer = new
            {
                first_name = firstName,
                last_name = lastName,
                email,
                reference
            }
        };

        using var response = await SendAsync(HttpMethod.Post, "customers.json", payload, cancellationToken);
        await EnsureSuccessAsync(response);

        var envelope = await DeserializeAsync<MaxioEnvelope<MaxioCustomer>>(response, cancellationToken)
            ?? throw new MaxioApiException((int)System.Net.HttpStatusCode.OK, string.Empty, "Empty customer response.");

        return envelope.Customer!;
    }

    /// <summary>
    /// Returns the subscription whose app-side reference matches, or null when none exists.
    /// </summary>
    public async Task<MaxioSubscription?> LookupSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get,
            $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}",
            retry: true, cancellationToken: cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        await EnsureSuccessAsync(response);

        var envelope = await DeserializeAsync<MaxioEnvelope<MaxioSubscription>>(response, cancellationToken);
        return envelope?.Subscription;
    }

    /// <summary>
    /// Reads a subscription by its Maxio id. Returns null when it no longer exists.
    /// </summary>
    public async Task<MaxioSubscription?> ReadSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get,
            $"subscriptions/{subscriptionId}.json",
            retry: true, cancellationToken: cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        await EnsureSuccessAsync(response);

        var envelope = await DeserializeAsync<MaxioEnvelope<MaxioSubscription>>(response, cancellationToken);
        return envelope?.Subscription;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle, string reference, CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            subscription = new
            {
                product_handle = productHandle,
                customer_id = customerId,
                reference,
                // Invoice/remittance collection: the seeded plans do not require a
                // payment method at signup, so no card capture (or 3-DS) is needed.
                payment_collection_method = "invoice"
            }
        };

        using var response = await SendAsync(HttpMethod.Post, "subscriptions.json", payload, cancellationToken);
        await EnsureSuccessAsync(response);

        var envelope = await DeserializeAsync<MaxioEnvelope<MaxioSubscription>>(response, cancellationToken)
            ?? throw new MaxioApiException((int)System.Net.HttpStatusCode.OK, string.Empty, "Empty subscription response.");

        return envelope.Subscription!;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, object? payload = null, CancellationToken cancellationToken = default, bool retry = false)
    {
        var attempts = retry ? GetMaxAttempts : 1;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(method, url);
                if (payload is not null)
                {
                    request.Content = new StringContent(JsonSerializer.Serialize(payload, _jsonOptions), Encoding.UTF8, "application/json");
                }

                var response = await _httpClient.SendAsync(request, cancellationToken);

                if (attempt < attempts && (int)response.StatusCode >= 500)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
                    continue;
                }
                return response;
            }
            catch (HttpRequestException) when (attempt < attempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < attempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
            }
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }
        var body = await response.Content.ReadAsStringAsync();
        throw new MaxioApiException((int)response.StatusCode, body, Truncate(body));
    }

    private static string Truncate(string value) =>
        string.IsNullOrEmpty(value) ? string.Empty : value.Length <= 500 ? value : value[..500];

    private static async Task<T?> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken) where T : class
    {
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, _jsonOptions, cancellationToken);
    }

    /// <summary>
    /// Parses list endpoints defensively: each element is either the bare
    /// resource object or an envelope with a single named property.
    /// </summary>
    private static async Task<List<T>> ParseListAsync<T>(HttpResponseMessage response, string? envelopeKey, CancellationToken cancellationToken) where T : class
    {
        var results = new List<T>();
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return results;
        }

        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (envelopeKey is not null &&
                element.ValueKind == JsonValueKind.Object &&
                element.TryGetProperty(envelopeKey, out var nested))
            {
                var wrapped = nested.Deserialize<T>(_jsonOptions);
                if (wrapped is not null)
                {
                    results.Add(wrapped);
                }
            }
            else
            {
                var direct = element.Deserialize<T>(_jsonOptions);
                if (direct is not null)
                {
                    results.Add(direct);
                }
            }
        }

        return results;
    }

    private sealed class MaxioEnvelope<T>
    {
        public T? Customer { get; set; }
        public T? Subscription { get; set; }
    }
}
