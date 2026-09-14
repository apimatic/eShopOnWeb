using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <inheritdoc cref="IMaxioApiClient" />
public class MaxioApiClient : IMaxioApiClient
{
    /// <summary>Maxio caps per_page at 200.</summary>
    private const int MaxPageSize = 200;

    /// <summary>Guards against an unbounded loop if the upstream ever stops honouring pagination.</summary>
    private const int MaxPages = 50;

    private static readonly HashSet<HttpStatusCode> RetryableStatusCodes = new()
    {
        HttpStatusCode.RequestTimeout,
        HttpStatusCode.TooManyRequests,
        HttpStatusCode.InternalServerError,
        HttpStatusCode.BadGateway,
        HttpStatusCode.ServiceUnavailable,
        HttpStatusCode.GatewayTimeout
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, IOptions<MaxioSettings> settings, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<MaxioSite?> ReadSiteAsync(CancellationToken cancellationToken = default)
    {
        var envelope = await SendAsync<MaxioSiteEnvelope>(HttpMethod.Get, "site.json", content: null, cancellationToken);
        return envelope?.Site;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        var products = new List<MaxioProduct>();
        var familySegment = "handle:" + Uri.EscapeDataString(productFamilyHandle);

        for (var page = 1; page <= MaxPages; page++)
        {
            var path = $"product_families/{familySegment}/products.json?page={page}&per_page={MaxPageSize}";
            var envelopes = await SendAsync<List<MaxioProductEnvelope>>(HttpMethod.Get, path, content: null, cancellationToken)
                            ?? new List<MaxioProductEnvelope>();

            foreach (var envelope in envelopes)
            {
                if (envelope.Product is not null)
                {
                    products.Add(envelope.Product);
                }
            }

            if (envelopes.Count < MaxPageSize)
            {
                break;
            }
        }

        return products;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";

        try
        {
            var envelope = await SendAsync<MaxioCustomerEnvelope>(HttpMethod.Get, path, content: null, cancellationToken);
            return envelope?.Customer;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // "No customer with this reference" is an expected answer, not a failure.
            return null;
        }
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomerRequest request, CancellationToken cancellationToken = default)
    {
        var envelope = await SendAsync<MaxioCustomerEnvelope>(HttpMethod.Post, "customers.json", request, cancellationToken);

        return envelope?.Customer
               ?? throw new MaxioApiException(HttpStatusCode.OK, "POST customers.json", new[] { "Maxio returned an empty customer payload." });
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        var path = $"customers/{customerId.ToString(CultureInfo.InvariantCulture)}/subscriptions.json";
        var envelopes = await SendAsync<List<MaxioSubscriptionEnvelope>>(HttpMethod.Get, path, content: null, cancellationToken)
                        ?? new List<MaxioSubscriptionEnvelope>();

        var subscriptions = new List<MaxioSubscription>(envelopes.Count);
        foreach (var envelope in envelopes)
        {
            if (envelope.Subscription is not null)
            {
                subscriptions.Add(envelope.Subscription);
            }
        }

        return subscriptions;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Post, "subscriptions.json", request, cancellationToken);

        return envelope?.Subscription
               ?? throw new MaxioApiException(HttpStatusCode.OK, "POST subscriptions.json", new[] { "Maxio returned an empty subscription payload." });
    }

    /// <summary>
    /// Issues one Maxio call, retrying throttled and transient failures. The request message is
    /// rebuilt on every attempt so retried writes always carry fresh, undisposed content.
    /// </summary>
    private async Task<TResponse?> SendAsync<TResponse>(HttpMethod method, string relativePath, object? content, CancellationToken cancellationToken)
    {
        var description = $"{method.Method} {StripQuery(relativePath)}";
        var maxAttempts = Math.Max(0, _settings.MaxRetryAttempts) + 1;

        for (var attempt = 1; ; attempt++)
        {
            HttpResponseMessage? response = null;
            Exception? transportFailure = null;

            try
            {
                using var request = new HttpRequestMessage(method, relativePath);
                if (content is not null)
                {
                    request.Content = JsonContent.Create(content, content.GetType(), options: MaxioJson.Options);
                }

                response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                transportFailure = ex;
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                // HttpClient surfaces its own timeout as a cancellation the caller never asked for.
                transportFailure = ex;
            }

            try
            {
                if (transportFailure is null && response!.IsSuccessStatusCode)
                {
                    return await DeserializeAsync<TResponse>(response, description, cancellationToken);
                }

                var isRetryable = transportFailure is not null || RetryableStatusCodes.Contains(response!.StatusCode);
                if (!isRetryable || attempt >= maxAttempts)
                {
                    if (transportFailure is not null)
                    {
                        throw new MaxioApiException(HttpStatusCode.ServiceUnavailable, description, new[] { "Could not reach the Maxio API." }, transportFailure);
                    }

                    throw new MaxioApiException(response!.StatusCode, description, await ReadErrorsAsync(response, cancellationToken));
                }

                var delay = ComputeRetryDelay(attempt, response);
                _logger.LogWarning(
                    "Maxio call {Request} attempt {Attempt}/{MaxAttempts} failed ({Outcome}); retrying in {DelayMs}ms.",
                    description,
                    attempt,
                    maxAttempts,
                    transportFailure is not null ? transportFailure.GetType().Name : ((int)response!.StatusCode).ToString(CultureInfo.InvariantCulture),
                    (int)delay.TotalMilliseconds);

                await Task.Delay(delay, cancellationToken);
            }
            finally
            {
                response?.Dispose();
            }
        }
    }

    private static async Task<TResponse?> DeserializeAsync<TResponse>(HttpResponseMessage response, string description, CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return default;
        }

        try
        {
            return await response.Content.ReadFromJsonAsync<TResponse>(MaxioJson.Options, cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new MaxioApiException(response.StatusCode, description, new[] { "Maxio returned a response that could not be parsed." }, ex);
        }
    }

    private TimeSpan ComputeRetryDelay(int attempt, HttpResponseMessage? response)
    {
        var retryAfter = response?.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return delta;
        }

        if (retryAfter?.Date is { } date && date - DateTimeOffset.UtcNow > TimeSpan.Zero)
        {
            return date - DateTimeOffset.UtcNow;
        }

        var baseDelayMs = Math.Max(1, _settings.RetryBaseDelayMilliseconds);
        var backoffMs = baseDelayMs * Math.Pow(2, attempt - 1);
        var jitterMs = Random.Shared.Next(0, baseDelayMs);
        return TimeSpan.FromMilliseconds(Math.Min(backoffMs + jitterMs, 30_000));
    }

    /// <summary>
    /// Maxio reports failures as {"errors": ["..."]} or {"errors": {"field": "..."}}, and
    /// occasionally as {"error": "..."}. Anything else is reported as a bare status code.
    /// </summary>
    private static async Task<IReadOnlyCollection<string>> ReadErrorsAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var errors = new List<string>();

        string body;
        try
        {
            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception)
        {
            return errors;
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return errors;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("errors", out var errorsElement))
            {
                CollectErrors(errorsElement, errors);
            }

            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var errorElement) && errorElement.ValueKind == JsonValueKind.String)
            {
                errors.Add(errorElement.GetString()!);
            }
        }
        catch (JsonException)
        {
            // Non-JSON error bodies (an HTML error page from a proxy, say) are truncated rather than echoed whole.
            errors.Add(body.Length > 200 ? body[..200] : body);
        }

        return errors;
    }

    private static void CollectErrors(JsonElement element, List<string> errors)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                errors.Add(element.GetString()!);
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectErrors(item, errors);
                }

                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var before = errors.Count;
                    CollectErrors(property.Value, errors);
                    for (var i = before; i < errors.Count; i++)
                    {
                        errors[i] = $"{property.Name}: {errors[i]}";
                    }
                }

                break;
        }
    }

    private static string StripQuery(string relativePath)
    {
        var queryIndex = relativePath.IndexOf('?');
        return queryIndex < 0 ? relativePath : relativePath[..queryIndex];
    }
}
