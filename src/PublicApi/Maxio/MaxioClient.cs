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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Typed <see cref="HttpClient"/> for the Maxio (Advanced Billing) REST API.
/// Authenticates with HTTP Basic auth (API key as user name, "X" as password) as
/// documented at https://maxio.com docs. Idempotent operations (GET, and POSTs that
/// carry a uniqueness token) are retried on transient failures.
/// </summary>
public sealed class MaxioClient : IMaxioClient
{
    private const int MaxAttempts = 3;
    private const int NonSuccessMinStatus = 400;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly HashSet<int> RetryableStatusCodes = new() { 408, 429, 500, 502, 503, 504 };

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioClient> _logger;

    public MaxioClient(HttpClient httpClient, IOptions<MaxioOptions> options, ILogger<MaxioClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string?> GetSiteCurrencyAsync(CancellationToken cancellationToken)
    {
        var site = await SendAsync<MaxioSiteEnvelope>(HttpMethod.Get, "site.json", body: null, cancellationToken);
        return site?.Site?.Currency;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var envelope = await SendAsync<MaxioCustomerEnvelope>(HttpMethod.Get, path, body: null, cancellationToken, notFoundReturnsNull: true);
        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomerBody body, CancellationToken cancellationToken)
    {
        var envelope = await SendAsync<MaxioCustomerEnvelope>(HttpMethod.Post, "customers.json", body, cancellationToken);
        if (envelope?.Customer is null)
        {
            throw new MaxioApiException((int)HttpStatusCode.UnprocessableEntity,
                "Maxio accepted the customer request but returned no customer.");
        }

        return envelope.Customer;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListFamilyProductsAsync(string familyHandle, CancellationToken cancellationToken)
    {
        var path = $"product_families/handle:{Uri.EscapeDataString(familyHandle)}/products.json";
        var items = await SendAsync<List<MaxioProductEnvelope>>(HttpMethod.Get, path, body: null, cancellationToken);
        return items?.Where(i => i.Product is not null).Select(i => i.Product!).ToList() ?? new List<MaxioProduct>();
    }

    public async Task<MaxioProduct?> FindProductByHandleAsync(string apiHandle, CancellationToken cancellationToken)
    {
        var path = $"products/handle/{Uri.EscapeDataString(apiHandle)}.json";
        var envelope = await SendAsync<MaxioProductEnvelope>(HttpMethod.Get, path, body: null, cancellationToken, notFoundReturnsNull: true);
        return envelope?.Product;
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Get, path, body: null, cancellationToken, notFoundReturnsNull: true);
        return envelope?.Subscription;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscriptionBody body, CancellationToken cancellationToken)
    {
        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Post, "subscriptions.json", body, cancellationToken);
        if (envelope?.Subscription is null)
        {
            throw new MaxioApiException((int)HttpStatusCode.UnprocessableEntity,
                "Maxio accepted the subscription request but returned no subscription.");
        }

        return envelope.Subscription;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        var path = $"customers/{customerId}/subscriptions.json";
        var items = await SendAsync<List<MaxioSubscriptionEnvelope>>(HttpMethod.Get, path, body: null, cancellationToken);
        return items?.Where(i => i.Subscription is not null).Select(i => i.Subscription!).ToList() ?? new List<MaxioSubscription>();
    }

    private async Task<T?> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken, bool notFoundReturnsNull = false)
    {
        using var requestContent = body is null ? null : Serialize(body);

        for (int attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var request = new HttpRequestMessage(method, BuildRequestUri(path));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", BuildBasicAuthValue());
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (requestContent is not null)
            {
                request.Content = requestContent;
            }

            HttpResponseMessage? response = null;
            try
            {
                response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            }
            catch (Exception ex) when (attempt < MaxAttempts && IsTransient(ex))
            {
                _logger.LogWarning(ex, "Maxio request to {Method} {Path} failed transiently (attempt {Attempt}).", method, path, attempt);
                await Backoff(attempt, cancellationToken);
                continue;
            }

            if (response.IsSuccessStatusCode)
            {
                using (response)
                {
                    return await ReadSuccessAsync<T>(response, cancellationToken);
                }
            }

            if (notFoundReturnsNull && response.StatusCode == HttpStatusCode.NotFound)
            {
                response.Dispose();
                return default;
            }

            var maxioError = await ReadErrorAsync(response, cancellationToken);
            var maxioStatus = (int)response.StatusCode;
            response.Dispose();

            if (attempt < MaxAttempts && RetryableStatusCodes.Contains(maxioStatus))
            {
                _logger.LogWarning("Maxio request to {Method} {Path} returned {StatusCode} (attempt {Attempt}).",
                    method, path, maxioStatus, attempt);
                await Backoff(attempt, cancellationToken);
                continue;
            }

            throw new MaxioApiException(maxioStatus, maxioError.Message, maxioError.Errors);
        }
    }

    private static async Task<T> ReadSuccessAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(content))
        {
            return default!;
        }

        var parsed = JsonSerializer.Deserialize<T>(content, JsonOptions);
        return parsed is null ? default! : parsed;
    }

    private static async Task<(string Message, IReadOnlyList<string> Errors)> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(content))
        {
            try
            {
                using var document = JsonDocument.Parse(content);
                if (document.RootElement.TryGetProperty("errors", out var errorsElement))
                {
                    var errors = new List<string>();
                    if (errorsElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in errorsElement.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.String)
                            {
                                errors.Add(item.GetString()!);
                            }
                        }
                    }
                    else if (errorsElement.ValueKind == JsonValueKind.String)
                    {
                        errors.Add(errorsElement.GetString()!);
                    }

                    if (errors.Count > 0)
                    {
                        return (string.Join(" ", errors), errors);
                    }
                }
            }
            catch (JsonException)
            {
                // fall through to the raw body below
            }

            return (content.Trim(), Array.Empty<string>());
        }

        return ($"Maxio returned HTTP {(int)response.StatusCode}.", Array.Empty<string>());
    }

    private Uri BuildRequestUri(string path)
    {
        var baseAddress = _options.BuildApiBaseAddress();
        return new Uri(baseAddress, path);
    }

    private string BuildBasicAuthValue()
    {
        var credentials = $"{_options.ResolveApiKey()}:X";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials));
    }

    private static HttpContent Serialize(object body)
    {
        var json = JsonSerializer.Serialize(body, body.GetType(), JsonOptions);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    private static bool IsTransient(Exception exception)
    {
        return exception is HttpRequestException
               || exception is TaskCanceledException
               || exception is OperationCanceledException;
    }

    private static Task Backoff(int attempt, CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromMilliseconds(300 * Math.Pow(2, attempt));
        return Task.Delay(delay, cancellationToken);
    }
}
