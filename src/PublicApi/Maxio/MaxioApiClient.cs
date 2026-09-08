using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Thin HTTP client for the Maxio Advanced Billing REST API (formerly Chargify).
///
/// The Maxio contract was confirmed against the official documentation/SDK
/// (github.com/maxio-com/ab-dotnet-sdk) and validated live against the sandbox site:
///   - auth: HTTP Basic, username = API key, password = "x"
///   - base URL: https://{subdomain}.chargify.com  (US) — see <see cref="MaxioOptions"/>
///   - list products of a family:    GET  /product_families/handle:{handle}/products.json
///   - lookup customer by reference: GET  /customers/lookup.json?reference={reference}
///   - create customer:              POST /customers.json
///   - create subscription:          POST /subscriptions.json
///   - list customer subscriptions:  GET  /customers/{customer_id}/subscriptions.json
/// All payloads are JSON.
/// </summary>
public sealed class MaxioApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly string _productFamilyHandle;

    public MaxioApiClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        MaxioOptions value = options.Value;

        if (string.IsNullOrWhiteSpace(value.ApiKey))
        {
            throw new MaxioConfigurationException("Maxio is not configured. Set Maxio:ApiKey (from MAXIO_API_KEY).");
        }

        if (string.IsNullOrWhiteSpace(value.ProductFamilyHandle))
        {
            throw new MaxioConfigurationException("Maxio is not configured. Set Maxio:ProductFamilyHandle (from MAXIO_DEFAULT_PRODUCT_FAMILY).");
        }

        _httpClient = httpClient;
        _httpClient.BaseAddress ??= value.BaseAddress;
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{value.ApiKey}:x")));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _productFamilyHandle = value.ProductFamilyHandle;
    }

    /// <summary>Lists the products (plans) of the configured product family.</summary>
    public async Task<IReadOnlyList<MaxioProduct>> ListPlansAsync(CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get,
                $"/product_families/handle:{Uri.EscapeDataString(_productFamilyHandle)}/products.json"),
            idempotent: true,
            cancellationToken).ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        var envelopes = await ReadJsonAsync<List<MaxioProductEnvelope>>(response, cancellationToken).ConfigureAwait(false);

        return envelopes?
            .Where(e => e.Product is not null)
            .Select(e => e.Product!)
            .ToList() ?? new List<MaxioProduct>();
    }

    /// <summary>
    /// Finds a customer by its unique reference. Returns <c>null</c> when no such customer exists.
    /// </summary>
    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get,
                $"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}"),
            idempotent: true,
            cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        var envelope = await ReadJsonAsync<MaxioCustomerEnvelope>(response, cancellationToken).ConfigureAwait(false);
        return envelope?.Customer;
    }

    /// <summary>Creates a customer. Throws <see cref="MaxioApiException"/> if Maxio rejects the payload.</summary>
    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomer customer, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/customers.json")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { customer }, JsonOptions),
                Encoding.UTF8,
                "application/json")
        };

        using var response = await SendAsync(() => request, idempotent: false, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        var envelope = await ReadJsonAsync<MaxioCustomerEnvelope>(response, cancellationToken).ConfigureAwait(false);
        return envelope?.Customer
            ?? throw new MaxioApiException(response.StatusCode, "Maxio did not return the created customer.");
    }

    /// <summary>
    /// Creates a subscription for an existing customer. A future <paramref name="nextBillingAt"/> avoids an
    /// immediate charge, which is what allows these demo plans ("payment method not required") to be subscribed
    /// to without card capture / 3-DS. Confirmed live against the sandbox: with a future next_billing_at and no
    /// payment profile Maxio returns a 201 "active" subscription and captures no payment.
    /// </summary>
    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        string productHandle,
        string customerReference,
        DateTimeOffset nextBillingAt,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            subscription = new
            {
                product_handle = productHandle,
                customer_reference = customerReference,
                next_billing_at = nextBillingAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ")
            }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/subscriptions.json")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload, JsonOptions),
                Encoding.UTF8,
                "application/json")
        };

        using var response = await SendAsync(() => request, idempotent: false, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        var envelope = await ReadJsonAsync<MaxioSubscriptionEnvelope>(response, cancellationToken).ConfigureAwait(false);
        return envelope?.Subscription
            ?? throw new MaxioApiException(response.StatusCode, "Maxio did not return the created subscription.");
    }

    /// <summary>Lists all subscriptions that belong to a customer.</summary>
    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        long customerId,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get, $"/customers/{customerId}/subscriptions.json"),
            idempotent: true,
            cancellationToken).ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        var envelopes = await ReadJsonAsync<List<MaxioSubscriptionEnvelope>>(response, cancellationToken).ConfigureAwait(false);

        return envelopes?
            .Where(e => e.Subscription is not null)
            .Select(e => e.Subscription!)
            .ToList() ?? new List<MaxioSubscription>();
    }

    private async Task<HttpResponseMessage> SendAsync(
        Func<HttpRequestMessage> requestFactory,
        bool idempotent,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;
        for (int attempt = 1; ; attempt++)
        {
            using var request = requestFactory();

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (HttpRequestException) when (idempotent && attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(300 * attempt), cancellationToken).ConfigureAwait(false);
                continue;
            }

            bool transient = idempotent
                && attempt < maxAttempts
                && response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
                    or HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway
                    or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;

            if (!transient)
            {
                return response;
            }

            response.Dispose();
            await Task.Delay(TimeSpan.FromMilliseconds(300 * attempt), cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string? body = null;
        try
        {
            body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // ignore - the status code is still surfaced below
        }

        throw new MaxioApiException(
            response.StatusCode,
            ExtractErrorMessage(body) ?? $"Maxio API returned {(int)response.StatusCode} {response.ReasonPhrase}.",
            body);
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Pulls the human readable error out of a Maxio error body like <c>{"errors": [...]}</c>.</summary>
    private static string? ExtractErrorMessage(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("errors", out JsonElement errors))
            {
                return errors.ValueKind switch
                {
                    JsonValueKind.Array => string.Join(" ", errors.EnumerateArray().Select(e => e.GetString()).Where(s => !string.IsNullOrEmpty(s))),
                    JsonValueKind.String => errors.GetString(),
                    _ => body
                };
            }
        }
        catch (JsonException)
        {
            // fall through
        }

        return body;
    }
}
