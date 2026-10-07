using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public class MaxioApiClient : IMaxioApiClient
{
    private const int PageSize = 200;
    private const int MaxPages = 50;
    private const int MaxGetAttempts = 3;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient http, IOptions<MaxioOptions> options, ILogger<MaxioApiClient> logger)
    {
        _http = http;
        _logger = logger;
        var settings = options.Value;

        _http.BaseAddress ??= settings.ResolveBaseAddress();
        _http.Timeout = TimeSpan.FromSeconds(Math.Max(1, settings.RequestTimeoutSeconds));
        _http.DefaultRequestHeaders.Accept.Clear();
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:x"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(string productFamilyHandle, CancellationToken cancellationToken)
    {
        var family = Uri.EscapeDataString($"handle:{productFamilyHandle}");
        var products = new List<MaxioProduct>();

        for (var page = 1; page <= MaxPages; page++)
        {
            var path = $"product_families/{family}/products.json?per_page={PageSize}&page={page}";
            var envelopes = await GetAsync<List<MaxioProductEnvelope>>(path, cancellationToken)
                            ?? new List<MaxioProductEnvelope>();

            foreach (var envelope in envelopes)
            {
                if (envelope.Product != null)
                {
                    products.Add(envelope.Product);
                }
            }

            if (envelopes.Count < PageSize)
            {
                break;
            }
        }

        return products;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var envelope = await GetAsync<MaxioCustomerEnvelope>(
                $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
            return envelope?.Customer;
        }
        catch (BillingProviderException ex) when (ex.Kind == BillingFailureKind.NotFound)
        {
            return null;
        }
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioNewCustomer customer, CancellationToken cancellationToken)
    {
        var envelope = await SendAsync<MaxioCustomerEnvelope>(
            HttpMethod.Post, "customers.json", new MaxioCreateCustomerRequest { Customer = customer }, cancellationToken);
        return envelope?.Customer
               ?? throw new BillingProviderException(BillingFailureKind.Unavailable, "Billing provider returned an empty customer response.");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        var envelopes = await GetAsync<List<MaxioSubscriptionEnvelope>>($"customers/{customerId}/subscriptions.json", cancellationToken)
                        ?? new List<MaxioSubscriptionEnvelope>();
        var result = new List<MaxioSubscription>();
        foreach (var envelope in envelopes)
        {
            if (envelope.Subscription != null)
            {
                result.Add(envelope.Subscription);
            }
        }

        return result;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioNewSubscription subscription, CancellationToken cancellationToken)
    {
        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(
            HttpMethod.Post, "subscriptions.json", new MaxioCreateSubscriptionRequest { Subscription = subscription }, cancellationToken);
        return envelope?.Subscription
               ?? throw new BillingProviderException(BillingFailureKind.Unavailable, "Billing provider returned an empty subscription response.");
    }

    private Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken) where T : class
        => SendAsync<T>(HttpMethod.Get, path, null, cancellationToken);

    private async Task<T?> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken) where T : class
    {
        var attempts = method == HttpMethod.Get ? MaxGetAttempts : 1;

        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(method, BuildUri(path));
            if (body != null)
            {
                request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
            }

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                if (attempt < attempts)
                {
                    await Task.Delay(BackoffFor(attempt, null), cancellationToken);
                    continue;
                }

                _logger.LogWarning(ex, "Maxio request {Method} {Path} failed to complete", method, StripQuery(path));
                throw new BillingProviderException(BillingFailureKind.Unavailable, "The billing provider is currently unreachable.", inner: ex);
            }

            using (response)
            {
                if (response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        return null;
                    }

                    var payload = await response.Content.ReadAsStringAsync(cancellationToken);
                    try
                    {
                        return JsonSerializer.Deserialize<T>(payload, JsonOptions);
                    }
                    catch (JsonException ex)
                    {
                        throw new BillingProviderException(BillingFailureKind.Unavailable, "The billing provider returned an unreadable response.", inner: ex);
                    }
                }

                var transient = response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500;
                if (transient && attempt < attempts)
                {
                    await Task.Delay(BackoffFor(attempt, response), cancellationToken);
                    continue;
                }

                throw await ToExceptionAsync(method, path, response, cancellationToken);
            }
        }
    }

    private Uri BuildUri(string path)
    {
        var baseUri = _http.BaseAddress!.AbsoluteUri.TrimEnd('/');
        return new Uri($"{baseUri}/{path}", UriKind.Absolute);
    }

    private static TimeSpan BackoffFor(int attempt, HttpResponseMessage? response)
    {
        var retryAfter = response?.Headers.RetryAfter?.Delta;
        if (retryAfter.HasValue)
        {
            return retryAfter.Value > TimeSpan.FromSeconds(5) ? TimeSpan.FromSeconds(5) : retryAfter.Value;
        }

        return TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt - 1));
    }

    private async Task<BillingProviderException> ToExceptionAsync(HttpMethod method, string path, HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        var messages = ParseErrors(raw);
        var status = (int)response.StatusCode;

        _logger.LogWarning("Maxio request {Method} {Path} returned {Status}", method, StripQuery(path), status);

        return response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                new BillingProviderException(BillingFailureKind.Misconfigured, "The billing provider rejected the configured credentials."),
            HttpStatusCode.NotFound =>
                new BillingProviderException(BillingFailureKind.NotFound, "The requested billing resource was not found.", messages),
            HttpStatusCode.TooManyRequests =>
                new BillingProviderException(BillingFailureKind.Unavailable, "The billing provider is rate limiting requests. Try again shortly."),
            _ when status >= 500 =>
                new BillingProviderException(BillingFailureKind.Unavailable, "The billing provider is temporarily unavailable."),
            _ =>
                new BillingProviderException(BillingFailureKind.Rejected,
                    messages.Count > 0 ? string.Join(" ", messages) : "The billing provider rejected the request.", messages)
        };
    }

    private static IReadOnlyList<string> ParseErrors(string raw)
    {
        var messages = new List<string>();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return messages;
        }

        try
        {
            var body = JsonSerializer.Deserialize<MaxioErrorBody>(raw, JsonOptions);
            if (body == null)
            {
                return messages;
            }

            Collect(body.Errors, null, messages);
        }
        catch (JsonException)
        {
        }

        return messages;
    }

    private static void Collect(JsonElement element, string? key, List<string> messages)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var text = element.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    messages.Add(string.IsNullOrEmpty(key) || key == "base" ? text : $"{key}: {text}");
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Collect(item, key, messages);
                }

                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    Collect(property.Value, property.Name, messages);
                }

                break;
        }
    }

    private static string StripQuery(string path)
    {
        var index = path.IndexOf('?');
        return index < 0 ? path : path[..index];
    }
}
