using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public sealed class MaxioBillingClient : IMaxioBillingClient
{
    private const string JsonContentType = "application/json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioBillingClient> _logger;

    public MaxioBillingClient(HttpClient httpClient, ILogger<MaxioBillingClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<MaxioProductFamily?> GetProductFamilyByHandleAsync(string productFamilyHandle, CancellationToken cancellationToken)
    {
        string path = $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}.json";
        string json = await GetStringAsync(path, cancellationToken).ConfigureAwait(false);
        return Deserialize<MaxioProductFamilyEnvelope>(json)?.ProductFamily;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsByFamilyHandleAsync(string productFamilyHandle, CancellationToken cancellationToken)
    {
        string path = $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json?include_archived=false";
        string json = await GetStringAsync(path, cancellationToken).ConfigureAwait(false);
        var response = Deserialize<List<MaxioProductEnvelope>>(json);
        return response?
                   .Select(item => item.Product)
                   .Where(product => product is not null)
                   .Cast<MaxioProduct>()
                   .ToList()
               ?? new List<MaxioProduct>();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        string path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        try
        {
            string json = await GetStringAsync(path, cancellationToken).ConfigureAwait(false);
            return Deserialize<MaxioCustomerEnvelope>(json)?.Customer;
        }
        catch (MaxioApiException ex) when (IsNotFound(ex))
        {
            return null;
        }
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(string firstName, string lastName, string email, string reference, CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, object>
        {
            ["customer"] = new Dictionary<string, object>
            {
                ["first_name"] = firstName,
                ["last_name"] = lastName,
                ["email"] = email,
                ["reference"] = reference
            }
        };

        string json = await PostAsync("customers.json", payload, cancellationToken).ConfigureAwait(false);
        return Deserialize<MaxioCustomerEnvelope>(json)?.Customer
            ?? throw new MaxioApiException(200, new[] { "The Billing API response did not include a customer." }, "The Billing API response did not include a customer.");
    }

    public async Task<MaxioSite> GetSiteAsync(CancellationToken cancellationToken)
    {
        string json = await GetStringAsync("site.json", cancellationToken).ConfigureAwait(false);
        return Deserialize<MaxioSiteEnvelope>(json)?.Site
            ?? throw new MaxioApiException(200, new[] { "The Billing API response did not include site settings." }, "The Billing API response did not include site settings.");
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, long customerId, string paymentCollectionMethod, CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, object>
        {
            ["subscription"] = new Dictionary<string, object>
            {
                ["product_handle"] = productHandle,
                ["customer_id"] = customerId,
                ["payment_collection_method"] = paymentCollectionMethod
            }
        };

        string json = await PostAsync("subscriptions.json", payload, cancellationToken).ConfigureAwait(false);
        return Deserialize<MaxioSubscriptionEnvelope>(json)?.Subscription
            ?? throw new MaxioApiException(200, new[] { "The Billing API response did not include a subscription." }, "The Billing API response did not include a subscription.");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        string path = $"customers/{customerId}/subscriptions.json";
        string json = await GetStringAsync(path, cancellationToken).ConfigureAwait(false);
        var response = Deserialize<List<MaxioSubscriptionEnvelope>>(json);
        return response?
                   .Select(item => item.Subscription)
                   .Where(subscription => subscription is not null)
                   .Cast<MaxioSubscription>()
                   .ToList()
               ?? new List<MaxioSubscription>();
    }

    private async Task<string> GetStringAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, path, content: null, cancellationToken).ConfigureAwait(false);
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> PostAsync(string path, object payload, CancellationToken cancellationToken)
    {
        string body = JsonSerializer.Serialize(payload, JsonOptions);
        using var content = new StringContent(body, System.Text.Encoding.UTF8, JsonContentType);
        using var response = await SendAsync(HttpMethod.Post, path, content, cancellationToken).ConfigureAwait(false);
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, HttpContent? content, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = content
        };

        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        string raw = string.Empty;
        try
        {
            raw = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to read the Maxio error response body for {Method} {Path}.", method, path);
        }

        response.Dispose();

        var errors = ParseErrors(raw);
        string detail = errors.Count > 0 ? string.Join(" ", errors) : raw;
        throw new MaxioApiException(
            (int)response.StatusCode,
            errors,
            $"The Maxio Billing API request {method} '{path}' failed with status {(int)response.StatusCode}{(detail.Length > 0 ? $": {detail}" : string.Empty)}.");
    }

    private static bool IsNotFound(MaxioApiException ex)
    {
        if (ex.StatusCode == (int)HttpStatusCode.NotFound)
        {
            return true;
        }

        return ex.StatusCode == (int)HttpStatusCode.UnprocessableEntity
            && (ex.Errors.Any(error => error.Contains("not found", StringComparison.OrdinalIgnoreCase))
                || ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> ParseErrors(string raw)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return errors;
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            JsonElement root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("errors", out JsonElement errorElement))
            {
                if (errorElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement item in errorElement.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            string? message = item.GetString();
                            if (!string.IsNullOrWhiteSpace(message))
                            {
                                errors.Add(message);
                            }
                        }
                    }
                }
                else if (errorElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty property in errorElement.EnumerateObject())
                    {
                        errors.Add($"{property.Name}: {property.Value}");
                    }
                }
                else if (errorElement.ValueKind == JsonValueKind.String)
                {
                    errors.Add(errorElement.GetString() ?? string.Empty);
                }
            }
        }
        catch (JsonException)
        {
            // The body was not JSON; callers fall back to the raw body.
        }

        if (errors.Count == 0 && !string.IsNullOrWhiteSpace(raw))
        {
            errors.Add(raw.Trim());
        }

        return errors;
    }

    private static T? Deserialize<T>(string json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }
}
