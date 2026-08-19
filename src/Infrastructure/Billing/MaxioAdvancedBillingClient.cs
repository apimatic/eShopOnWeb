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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public class MaxioAdvancedBillingClient : IMaxioAdvancedBillingClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioAdvancedBillingClient> _logger;

    public MaxioAdvancedBillingClient(HttpClient httpClient, ILogger<MaxioAdvancedBillingClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken)
    {
        var familyId = "handle:" + productFamilyHandle;
        var products = new List<MaxioProduct>();
        var page = 1;
        const int perPage = 200;

        while (true)
        {
            var path = $"product_families/{familyId}/products.json?page={page}&per_page={perPage}";
            var wrappers = await SendAsync<List<MaxioProductResponse>>(HttpMethod.Get, path, null, cancellationToken, allowNotFound: false);

            var batch = wrappers?
                .Where(w => w?.Product != null)
                .Select(w => w.Product)
                .ToList() ?? new List<MaxioProduct>();

            products.AddRange(batch);

            if (batch.Count < perPage)
            {
                break;
            }

            page++;
        }

        return products;
    }

    public async Task<MaxioCustomer> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var wrapper = await SendAsync<MaxioCustomerResponse>(HttpMethod.Get, path, null, cancellationToken, allowNotFound: true);
        return wrapper?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken)
    {
        var body = new MaxioCreateCustomerRequest { Customer = customer };
        var wrapper = await SendAsync<MaxioCustomerResponse>(HttpMethod.Post, "customers.json", body, cancellationToken, allowNotFound: false);
        if (wrapper?.Customer == null)
        {
            throw new BillingGatewayException("Maxio created a customer but returned an empty body.");
        }

        return wrapper.Customer;
    }

    public async Task<MaxioSubscription> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var wrapper = await SendAsync<MaxioSubscriptionResponse>(HttpMethod.Get, path, null, cancellationToken, allowNotFound: true);
        return wrapper?.Subscription;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        var path = $"customers/{customerId}/subscriptions.json";
        var wrappers = await SendAsync<List<MaxioSubscriptionResponse>>(HttpMethod.Get, path, null, cancellationToken, allowNotFound: true);
        if (wrappers == null)
        {
            return Array.Empty<MaxioSubscription>();
        }

        return wrappers.Where(w => w?.Subscription != null).Select(w => w.Subscription).ToList();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken cancellationToken)
    {
        var body = new MaxioCreateSubscriptionRequest { Subscription = subscription };
        var wrapper = await SendAsync<MaxioSubscriptionResponse>(HttpMethod.Post, "subscriptions.json", body, cancellationToken, allowNotFound: false);
        if (wrapper?.Subscription == null)
        {
            throw new BillingGatewayException("Maxio created a subscription but returned an empty body.");
        }

        return wrapper.Subscription;
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string relativePath, object body, CancellationToken cancellationToken, bool allowNotFound)
        where T : class
    {
        var isGet = method == HttpMethod.Get;
        const int maxAttempts = 3;
        HttpResponseMessage response = null;
        Exception lastTransport = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(method, relativePath);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                if (body != null)
                {
                    request.Content = JsonContent.Create(body, options: JsonOptions);
                }

                response = await _httpClient.SendAsync(request, cancellationToken);

                if (isGet && IsTransient(response.StatusCode) && attempt < maxAttempts)
                {
                    _logger.LogWarning("Transient Maxio HTTP {Status} on GET {Path}; retry {Attempt}.", (int)response.StatusCode, relativePath, attempt);
                    response.Dispose();
                    await Task.Delay(200 * attempt, cancellationToken);
                    continue;
                }

                lastTransport = null;
                break;
            }
            catch (HttpRequestException ex) when (isGet && attempt < maxAttempts)
            {
                lastTransport = ex;
                _logger.LogWarning(ex, "Transport error calling Maxio GET {Path}; retry {Attempt}.", relativePath, attempt);
                await Task.Delay(200 * attempt, cancellationToken);
            }
        }

        if (response == null)
        {
            throw new BillingGatewayException("Unable to reach Maxio Advanced Billing.", 503, lastTransport);
        }

        using (response)
        {
            if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.NoContent)
                {
                    return null;
                }

                try
                {
                    return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
                }
                catch (JsonException ex)
                {
                    throw new BillingGatewayException("Maxio returned a response that could not be parsed.", 502, ex);
                }
            }

            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var message = ExtractErrorMessage(errorBody) ?? $"Maxio request failed with HTTP {(int)response.StatusCode}.";
            _logger.LogWarning("Maxio {Method} {Path} failed with HTTP {Status}: {Message}", method, SanitizePath(relativePath), (int)response.StatusCode, message);
            throw new BillingGatewayException(message, (int)response.StatusCode);
        }
    }

    private static bool IsTransient(HttpStatusCode statusCode)
        => statusCode == HttpStatusCode.TooManyRequests
           || statusCode == HttpStatusCode.BadGateway
           || statusCode == HttpStatusCode.ServiceUnavailable
           || statusCode == HttpStatusCode.GatewayTimeout;

    private static string SanitizePath(string path)
    {
        var queryIndex = path.IndexOf('?');
        return queryIndex >= 0 ? path.Substring(0, queryIndex) : path;
    }

    private static string ExtractErrorMessage(string errorBody)
    {
        if (string.IsNullOrWhiteSpace(errorBody))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(errorBody);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Truncate(errorBody);
            }

            if (document.RootElement.TryGetProperty("error", out var single) && single.ValueKind == JsonValueKind.String)
            {
                return single.GetString();
            }

            if (!document.RootElement.TryGetProperty("errors", out var errors))
            {
                return Truncate(errorBody);
            }

            if (errors.ValueKind == JsonValueKind.String)
            {
                return errors.GetString();
            }

            if (errors.ValueKind == JsonValueKind.Array)
            {
                var parts = new List<string>();
                foreach (var item in errors.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        parts.Add(item.GetString());
                    }
                }

                return parts.Count > 0 ? string.Join(" ", parts) : Truncate(errorBody);
            }

            if (errors.ValueKind == JsonValueKind.Object)
            {
                var parts = new List<string>();
                foreach (var property in errors.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in property.Value.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.String)
                            {
                                parts.Add($"{property.Name}: {item.GetString()}");
                            }
                        }
                    }
                    else if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        parts.Add($"{property.Name}: {property.Value.GetString()}");
                    }
                }

                return parts.Count > 0 ? string.Join(" ", parts) : Truncate(errorBody);
            }
        }
        catch (JsonException)
        {
            return Truncate(errorBody);
        }

        return Truncate(errorBody);
    }

    private static string Truncate(string value)
    {
        const int max = 500;
        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed.Substring(0, max);
    }
}
