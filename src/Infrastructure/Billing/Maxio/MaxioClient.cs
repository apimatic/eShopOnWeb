using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

public sealed class MaxioClient : IMaxioClient
{
    private static readonly SemaphoreSlim ConcurrencyGate = new(4, 4);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;

    public MaxioClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ApiKey}:X"));
        _httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
        _httpClient.Timeout = TimeSpan.FromSeconds(20);
    }

    public async Task<IReadOnlyList<MaxioProduct>> GetProductsAsync(CancellationToken cancellationToken)
    {
        var family = Uri.EscapeDataString($"handle:{_options.ProductFamilyHandle}");
        var envelopes = await SendAsync<List<MaxioProductEnvelope>>(
            HttpMethod.Get,
            $"product_families/{family}/products.json?per_page=200",
            null,
            cancellationToken);

        return envelopes!
            .Select(envelope => envelope.Product)
            .Where(product => product.ArchivedAt is null)
            .ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        var result = await SendAsync<MaxioCustomerEnvelope>(
            HttpMethod.Get,
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}",
            null,
            cancellationToken,
            HttpStatusCode.NotFound);
        return result?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken)
    {
        var body = new
        {
            customer = new
            {
                first_name = customer.FirstName,
                last_name = customer.LastName,
                email = customer.Email,
                reference = customer.Reference
            },
            uniqueness_token = customer.UniquenessToken
        };

        var result = await SendAsync<MaxioCustomerEnvelope>(
            HttpMethod.Post,
            "customers.json",
            body,
            cancellationToken);
        return result!.Customer;
    }

    public async Task<MaxioSubscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        var result = await SendAsync<MaxioSubscriptionEnvelope>(
            HttpMethod.Get,
            $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}",
            null,
            cancellationToken,
            HttpStatusCode.NotFound);
        return result?.Subscription;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken cancellationToken)
    {
        var body = new
        {
            subscription = new
            {
                product_handle = subscription.ProductHandle,
                customer_reference = subscription.CustomerReference,
                reference = subscription.Reference,
                payment_collection_method = "remittance"
            },
            uniqueness_token = subscription.UniquenessToken
        };

        var result = await SendAsync<MaxioSubscriptionEnvelope>(
            HttpMethod.Post,
            "subscriptions.json",
            body,
            cancellationToken);
        return result!.Subscription;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> GetCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken)
    {
        var envelopes = await SendAsync<List<MaxioSubscriptionEnvelope>>(
            HttpMethod.Get,
            $"customers/{customerId}/subscriptions.json",
            null,
            cancellationToken);
        return envelopes!.Select(envelope => envelope.Subscription).ToList();
    }

    private async Task<T?> SendAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken,
        params HttpStatusCode[] nullStatusCodes)
    {
        var serializedBody = body is null ? null : JsonSerializer.Serialize(body, JsonOptions);

        await ConcurrencyGate.WaitAsync(cancellationToken);
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                using var request = new HttpRequestMessage(method, BuildRequestUri(path));
                request.Headers.Accept.ParseAdd("application/json");
                if (serializedBody is not null)
                {
                    request.Content = new StringContent(serializedBody, Encoding.UTF8, "application/json");
                }

                HttpResponseMessage response;
                try
                {
                    response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                }
                catch (HttpRequestException) when (attempt < 2)
                {
                    await DelayBeforeRetryAsync(attempt, null, cancellationToken);
                    continue;
                }
                catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < 2)
                {
                    await DelayBeforeRetryAsync(attempt, null, cancellationToken);
                    continue;
                }

                using (response)
                {
                    if (nullStatusCodes.Contains(response.StatusCode))
                    {
                        return default;
                    }

                    if (IsTransient(response.StatusCode) && attempt < 2)
                    {
                        await DelayBeforeRetryAsync(attempt, response, cancellationToken);
                        continue;
                    }

                    var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new MaxioApiException(response.StatusCode, ExtractErrorMessage(responseBody));
                    }

                    var value = JsonSerializer.Deserialize<T>(responseBody, JsonOptions);
                    return value ?? throw new MaxioApiException(
                        HttpStatusCode.BadGateway,
                        "Maxio returned an empty or invalid response.");
                }
            }
        }
        finally
        {
            ConcurrencyGate.Release();
        }
    }

    private Uri BuildRequestUri(string path)
    {
        var baseUrl = _options.GetApiBaseUrl();
        return new Uri($"{baseUrl.TrimEnd('/')}/{path}", UriKind.Absolute);
    }

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.RequestTimeout ||
        statusCode == HttpStatusCode.TooManyRequests ||
        (int)statusCode >= 500;

    private static async Task DelayBeforeRetryAsync(
        int attempt,
        HttpResponseMessage? response,
        CancellationToken cancellationToken)
    {
        var retryAfter = response?.Headers.RetryAfter?.Delta;
        var delay = retryAfter ?? TimeSpan.FromMilliseconds(attempt == 0 ? 250 : 750);
        await Task.Delay(delay, cancellationToken);
    }

    private static string ExtractErrorMessage(string responseBody)
    {
        const string fallback = "Maxio rejected the billing request.";
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return fallback;
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (!document.RootElement.TryGetProperty("errors", out var errors))
            {
                return fallback;
            }

            var messages = new List<string>();
            CollectStrings(errors, messages);
            return messages.Count == 0 ? fallback : string.Join(" ", messages.Take(5));
        }
        catch (JsonException)
        {
            return fallback;
        }
    }

    private static void CollectStrings(JsonElement element, ICollection<string> messages)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var value = element.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    messages.Add(value);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectStrings(item, messages);
                }
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    CollectStrings(property.Value, messages);
                }
                break;
        }
    }
}
