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
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// <see cref="IMaxioClient"/> implemented against the Maxio Advanced Billing OpenAPI specification.
/// Auth: HTTP Basic with the API key as username and "x" as password (spec securitySchemes.BasicAuth).
/// Base address: spec server templating https://{site}.chargify.com (US) / https://{site}.ebilling.maxio.com (EU),
/// unless Maxio:BaseUrl overrides it.
/// </summary>
public class MaxioClient : IMaxioClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly HttpStatusCode[] TransientStatusCodes =
    {
        HttpStatusCode.RequestTimeout, // 408
        HttpStatusCode.TooManyRequests, // 429
        HttpStatusCode.InternalServerError, // 500
        HttpStatusCode.BadGateway, // 502
        HttpStatusCode.ServiceUnavailable, // 503
        HttpStatusCode.GatewayTimeout // 504
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioClient> _logger;

    public MaxioClient(HttpClient httpClient, IOptions<MaxioSettings> settingsOptions, ILogger<MaxioClient> logger)
    {
        var settings = settingsOptions.Value;
        settings.Validate();

        _httpClient = httpClient;
        _logger = logger;
        _httpClient.BaseAddress = settings.GetBaseAddress();

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var response = await SendWithRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Get, $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}"),
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var envelope = await ReadSuccessAsync<MaxioCustomerResponse>(response, cancellationToken);
        return envelope.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerAttributes customer, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Post, "customers.json")
            {
                Content = JsonContent.Create(new MaxioCreateCustomerRequest { Customer = customer }, options: JsonOptions)
            },
            cancellationToken);

        var envelope = await ReadSuccessAsync<MaxioCustomerResponse>(response, cancellationToken);
        return envelope.Customer ?? throw new MaxioApiException(response.StatusCode, new[] { "Maxio returned an empty customer payload." });
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        // The path parameter accepts "handle:{api-handle}" per the spec.
        var path = $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json";
        var response = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Get, path), cancellationToken);

        var envelopes = await ReadSuccessAsync<List<MaxioProductResponse>>(response, cancellationToken);
        return envelopes.Where(e => e.Product != null).Select(e => e.Product!).ToList();
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        var response = await SendWithRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Get, $"customers/{customerId}/subscriptions.json"),
            cancellationToken);

        var envelopes = await ReadSuccessAsync<List<MaxioSubscriptionResponse>>(response, cancellationToken);
        return envelopes.Where(e => e.Subscription != null).Select(e => e.Subscription!).ToList();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken cancellationToken = default)
    {
        // No automatic retry here: subscription creation is not idempotent server-side, so replaying
        // a timed-out request could double-enroll the customer. Idempotency is handled by the caller.
        var response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Post, "subscriptions.json")
            {
                Content = JsonContent.Create(new MaxioCreateSubscriptionRequest { Subscription = subscription }, options: JsonOptions)
            },
            cancellationToken);

        var envelope = await ReadSuccessAsync<MaxioSubscriptionResponse>(response, cancellationToken);
        return envelope.Subscription ?? throw new MaxioApiException(response.StatusCode, new[] { "Maxio returned an empty subscription payload." });
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(Func<HttpRequestMessage> requestFactory, CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            var response = await SendAsync(requestFactory, cancellationToken);
            if (!TransientStatusCodes.Contains(response.StatusCode) || attempt == maxAttempts)
            {
                return response;
            }

            _logger.LogWarning("Maxio GET returned transient status {StatusCode}; retrying (attempt {Attempt}/{MaxAttempts}).",
                (int)response.StatusCode, attempt, maxAttempts);
            response.Dispose();
            await Task.Delay(TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt - 1)), cancellationToken);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> requestFactory, CancellationToken cancellationToken)
    {
        using var request = requestFactory();
        try
        {
            return await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new MaxioApiException(HttpStatusCode.ServiceUnavailable, new[] { $"Could not reach the Maxio API: {ex.Message}" });
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MaxioApiException(HttpStatusCode.GatewayTimeout, new[] { $"The Maxio API did not respond in time: {ex.Message}" });
        }
    }

    private async Task<T> ReadSuccessAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var errors = await ReadErrorsAsync(response, cancellationToken);
            throw new MaxioApiException(response.StatusCode, errors);
        }

        var payload = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        return payload ?? throw new MaxioApiException(response.StatusCode, new[] { "Maxio returned an empty response body." });
    }

    /// <summary>
    /// The spec's error models carry "errors" either as an array of strings (Error-List-Response)
    /// or as a field-to-message object (Customer-Error-Response); both are normalized to strings.
    /// </summary>
    private static async Task<IReadOnlyList<string>> ReadErrorsAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("errors", out var errors))
            {
                return new[] { Truncate(body) };
            }

            if (errors.ValueKind == JsonValueKind.Array)
            {
                return errors.EnumerateArray().Select(e => e.ToString()).ToList();
            }

            if (errors.ValueKind == JsonValueKind.Object)
            {
                return errors.EnumerateObject().Select(p => $"{p.Name}: {p.Value}").ToList();
            }

            return new[] { errors.ToString() };
        }
        catch (JsonException)
        {
            return new[] { $"Status {(int)response.StatusCode} ({response.StatusCode})." };
        }
    }

    private static string Truncate(string value) => value.Length <= 500 ? value : value.Substring(0, 500);
}
