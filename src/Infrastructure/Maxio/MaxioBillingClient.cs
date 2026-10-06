using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public class MaxioBillingClient : IMaxioBillingClient
{
    private const int GetMaxAttempts = 3;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly IAppLogger<MaxioBillingClient> _logger;

    public MaxioBillingClient(HttpClient httpClient, IOptions<MaxioSettings> settings, IAppLogger<MaxioBillingClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(string productFamilyHandleOrId, CancellationToken cancellationToken = default)
    {
        var familyRoute = int.TryParse(productFamilyHandleOrId, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
            ? productFamilyHandleOrId
            : "handle:" + Uri.EscapeDataString(productFamilyHandleOrId);

        return GetAsync<IReadOnlyList<MaxioProduct>>(
            $"/product_families/{familyRoute}/products.json",
            body => Deserialize<List<ProductEnvelope>>(body)
                .Where(envelope => envelope?.Product is not null)
                .Select(envelope => envelope!.Product!.ToModel())
                .ToList(),
            cancellationToken);
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(BuildRequest(HttpMethod.Get, $"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}"), cancellationToken);
        var body = await ReadAsStringAsync(response, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        EnsureSuccessStatusCode(response, body);

        return Deserialize<CustomerEnvelope>(body).Customer?.ToModel();
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioNewCustomerRequest request, CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(new CustomerEnvelope
        {
            Customer = CustomerDto.From(request)
        }, SerializerOptions);

        var response = await SendAsync(BuildRequest(HttpMethod.Post, "/customers.json", payload), cancellationToken);
        var body = await ReadAsStringAsync(response, cancellationToken);
        EnsureSuccessStatusCode(response, body);

        var customer = Deserialize<CustomerEnvelope>(body).Customer;
        if (customer is null)
            throw new MaxioApiException((int)response.StatusCode, Array.Empty<string>(), Truncate(body));

        return customer.ToModel();
    }

    public Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsForCustomerAsync(int customerId, CancellationToken cancellationToken = default)
    {
        return GetAsync<IReadOnlyList<MaxioSubscription>>(
            $"/customers/{customerId}/subscriptions.json",
            body => Deserialize<List<SubscriptionEnvelope>>(body)
                .Where(envelope => envelope?.Subscription is not null)
                .Select(envelope => envelope!.Subscription!.ToModel())
                .ToList(),
            cancellationToken);
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioNewSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(new SubscriptionEnvelope
        {
            Subscription = new SubscriptionDto
            {
                ProductHandle = request.ProductHandle,
                CustomerId = request.CustomerId,
                Reference = request.Reference
            }
        }, SerializerOptions);

        var response = await SendAsync(BuildRequest(HttpMethod.Post, "/subscriptions.json", payload), cancellationToken);
        var body = await ReadAsStringAsync(response, cancellationToken);
        EnsureSuccessStatusCode(response, body);

        var subscription = Deserialize<SubscriptionEnvelope>(body).Subscription;
        if (subscription is null)
            throw new MaxioApiException((int)response.StatusCode, Array.Empty<string>(), Truncate(body));

        return subscription.ToModel();
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(BuildRequest(HttpMethod.Get, $"/subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}"), cancellationToken);
        var body = await ReadAsStringAsync(response, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        EnsureSuccessStatusCode(response, body);

        return Deserialize<SubscriptionEnvelope>(body).Subscription?.ToModel();
    }

    private async Task<TModel> GetAsync<TModel>(string path, Func<string, TModel> projector, CancellationToken cancellationToken)
        where TModel : class
    {
        Exception? lastTransient = null;

        for (int attempt = 1; attempt <= GetMaxAttempts; attempt++)
        {
            try
            {
                var response = await SendAsync(BuildRequest(HttpMethod.Get, path), cancellationToken);
                var body = await ReadAsStringAsync(response, cancellationToken);

                if (IsRetryableStatus(response.StatusCode) && attempt < GetMaxAttempts)
                {
                    LogTransient(path, $"responded {(int)response.StatusCode}", attempt);
                    await Task.Delay(Backoff(attempt), cancellationToken);
                    continue;
                }

                EnsureSuccessStatusCode(response, body);
                return projector(body);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                lastTransient = ex;
                if (attempt < GetMaxAttempts)
                {
                    LogTransient(path, "timed out", attempt);
                    await Task.Delay(Backoff(attempt), cancellationToken);
                    continue;
                }
            }
            catch (HttpRequestException ex)
            {
                lastTransient = ex;
                if (attempt < GetMaxAttempts)
                {
                    LogTransient(path, $"failed transiently ({ex.Message})", attempt);
                    await Task.Delay(Backoff(attempt), cancellationToken);
                    continue;
                }
            }
        }

        throw lastTransient ?? new MaxioApiException(0, new[] { $"Maxio GET {path} failed after {GetMaxAttempts} attempts." }, null);
    }

    private void LogTransient(string path, string reason, int attempt) =>
        _logger.LogWarning($"Maxio GET {path} {reason}; retry attempt {attempt} of {GetMaxAttempts}.");

    private static TimeSpan Backoff(int attempt) => TimeSpan.FromMilliseconds(300 * Math.Pow(3, attempt - 1));

    private static bool IsRetryableStatus(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.TooManyRequests ||
        statusCode == HttpStatusCode.RequestTimeout ||
        (int)statusCode >= 500;

    private HttpRequestMessage BuildRequest(HttpMethod method, string path, string? jsonPayload = null)
    {
        _settings.Validate();

        var request = new HttpRequestMessage(method, $"{_settings.ResolveBaseUrl()}{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ApiKey}:x")));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (jsonPayload is not null)
            request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        return request;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning($"Maxio API request {request.Method} {request.RequestUri?.AbsolutePath} failed: {ex.Message}");
            throw;
        }
    }

    private static Task<string> ReadAsStringAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        response.Content.ReadAsStringAsync(cancellationToken);

    private void EnsureSuccessStatusCode(HttpResponseMessage response, string body)
    {
        if (response.IsSuccessStatusCode)
            return;

        var errors = ParseErrors(body);
        _logger.LogWarning($"Maxio API responded {(int)response.StatusCode} for {response.RequestMessage?.Method} {response.RequestMessage?.RequestUri?.AbsolutePath}. Errors: {string.Join("; ", errors)}");
        throw new MaxioApiException((int)response.StatusCode, errors, Truncate(body));
    }

    private static IReadOnlyList<string> ParseErrors(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return Array.Empty<string>();

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("errors", out var errorsElement))
            {
                return errorsElement.ValueKind switch
                {
                    JsonValueKind.Array => errorsElement.EnumerateArray()
                        .Select(element => element.ValueKind == JsonValueKind.String ? element.GetString()! : element.GetRawText())
                        .ToList(),
                    JsonValueKind.Object => errorsElement.EnumerateObject()
                        .Select(property => $"{property.Name}: {(property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : property.Value.GetRawText())}")
                        .ToList(),
                    _ => new List<string> { errorsElement.GetRawText() }
                };
            }
        }
        catch (JsonException)
        {
        }

        return new List<string> { Truncate(body) };
    }

    private static T Deserialize<T>(string body) =>
        JsonSerializer.Deserialize<T>(body, SerializerOptions)
        ?? throw new MaxioApiException(0, new[] { $"Unexpected empty payload from Maxio: '{Truncate(body)}'" }, Truncate(body));

    private static string Truncate(string value, int max = 512) =>
        value.Length <= max ? value : value[..max] + "...";
}
