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
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// HTTP client for the Maxio Advanced Billing (Billing API). Uses HTTP Basic
/// authentication (API key as username, literal "X" as password) over TLS, as required
/// by the Billing API.
/// </summary>
public class MaxioApiClient : IMaxioApiClient
{
    private const int MaxPerPage = 200;

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;

    public MaxioApiClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsInFamilyAsync(string familyHandleOrId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(familyHandleOrId))
        {
            throw MaxioConfigurationException.MissingSettings("A product family handle is required to list subscription plans.");
        }

        var results = new List<MaxioProduct>();
        var page = 1;
        var familyReference = FormatFamilyReference(familyHandleOrId);
        while (true)
        {
            var url = $"product_families/{Uri.EscapeDataString(familyReference)}/products.json?page={page}&per_page={MaxPerPage}";
            var products = await GetListWrappedAsync<MaxioProduct>(url, "product", cancellationToken);
            results.AddRange(products);
            if (products.Count < MaxPerPage)
            {
                break;
            }
            page++;
        }

        return results;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var url = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        try
        {
            return await GetSingleWrappedAsync<MaxioCustomer>(url, "customer", cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerInput input, CancellationToken cancellationToken = default)
    {
        var body = new
        {
            customer = new
            {
                reference = input.Reference,
                first_name = input.FirstName,
                last_name = input.LastName,
                email = input.Email,
                organization = input.Organization
            }
        };
        return await SendSingleWrappedAsync<MaxioCustomer>(HttpMethod.Post, "customers.json", body, "customer", cancellationToken);
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var results = new List<MaxioSubscription>();
        var page = 1;
        while (true)
        {
            var url = $"customers/{customerId}/subscriptions.json?page={page}&per_page={MaxPerPage}";
            var subscriptions = await GetListWrappedAsync<MaxioSubscription>(url, "subscription", cancellationToken);
            results.AddRange(subscriptions);
            if (subscriptions.Count < MaxPerPage)
            {
                break;
            }
            page++;
        }

        return results;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle, string uniquenessToken, CancellationToken cancellationToken = default)
    {
        var body = new
        {
            subscription = new
            {
                customer_id = customerId,
                product_handle = productHandle,
                // The plans eShopOnWeb offers do not require a payment method at signup
                // (no card capture / 3-DS), so shoppers enroll on invoice (remittance)
                // billing rather than automatic card collection.
                payment_collection_method = "remittance"
            },
            // Billing API duplicate prevention: reusing the same uniqueness_token within
            // 60 minutes is rejected with 409 Conflict instead of creating a duplicate.
            uniqueness_token = uniquenessToken
        };
        return await SendSingleWrappedAsync<MaxioSubscription>(HttpMethod.Post, "subscriptions.json", body, "subscription", cancellationToken);
    }

    public Task<MaxioSubscription> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        return GetSingleWrappedAsync<MaxioSubscription>($"subscriptions/{subscriptionId}.json", "subscription", cancellationToken);
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw MaxioConfigurationException.MissingSettings("Maxio:ApiKey is required.");
        }

        // Resolving the base address validates that a subdomain (or base URL) was supplied.
        MaxioOptions.ResolveBaseUrl(_options);
    }

    /// <summary>
    /// Product family path segments accept either the numeric id or the handle prefixed
    /// with "handle:". A bare handle passed in configuration is prefixed automatically;
    /// a value already carrying the prefix (or a numeric id) is used verbatim.
    /// </summary>
    private static string FormatFamilyReference(string familyHandleOrId)
    {
        var value = familyHandleOrId.Trim();
        if (value.StartsWith("handle:", StringComparison.OrdinalIgnoreCase) || value.All(char.IsDigit))
        {
            return value;
        }

        return $"handle:{value}";
    }

    private Task<TWrapped> GetSingleWrappedAsync<TWrapped>(string relativeUrl, string wrapperProperty, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        return SendSingleWrappedAsync<TWrapped>(HttpMethod.Get, relativeUrl, null, wrapperProperty, cancellationToken);
    }

    private Task<IReadOnlyList<TWrapped>> GetListWrappedAsync<TWrapped>(string relativeUrl, string wrapperProperty, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        return SendListWrappedAsync<TWrapped>(HttpMethod.Get, relativeUrl, null, wrapperProperty, cancellationToken);
    }

    private async Task<TWrapped> SendSingleWrappedAsync<TWrapped>(HttpMethod method, string relativeUrl, object? body, string wrapperProperty, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(method, relativeUrl, body, cancellationToken);
        var content = await ReadAsync(response, relativeUrl, cancellationToken);

        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(wrapperProperty, out var property))
            {
                return property.Deserialize<TWrapped>(MaxioJson.Options)!;
            }

            throw new MaxioApiException(response.StatusCode,
                new[] { $"Billing API response for '{relativeUrl}' did not contain the expected '{wrapperProperty}' element." },
                content);
        }
        catch (JsonException ex)
        {
            throw new MaxioApiException(response.StatusCode, new[] { $"Billing API returned an unparseable response: {ex.Message}" }, content);
        }
    }

    private async Task<IReadOnlyList<TWrapped>> SendListWrappedAsync<TWrapped>(HttpMethod method, string relativeUrl, object? body, string wrapperProperty, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(method, relativeUrl, body, cancellationToken);
        var content = await ReadAsync(response, relativeUrl, cancellationToken);

        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Array)
            {
                throw new MaxioApiException(response.StatusCode,
                    new[] { $"Billing API response for '{relativeUrl}' was expected to be a list." },
                    content);
            }

            return root.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.Object && item.TryGetProperty(wrapperProperty, out _))
                .Select(item => item.GetProperty(wrapperProperty).Deserialize<TWrapped>(MaxioJson.Options)!)
                .ToList();
        }
        catch (JsonException ex)
        {
            throw new MaxioApiException(response.StatusCode, new[] { $"Billing API returned an unparseable response: {ex.Message}" }, content);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string relativeUrl, object? body, CancellationToken cancellationToken)
    {
        EnsureConfigured();

        using var request = new HttpRequestMessage(method, relativeUrl);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        // Billing API authentication: HTTP Basic with the API key as the username and
        // the literal "X" as the password.
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ApiKey}:X"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        if (body != null)
        {
            request.Content = JsonContent.Create(body, options: MaxioJson.Options);
        }

        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var statusCode = response.StatusCode;
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            response.Dispose();
            throw new MaxioApiException(statusCode, MaxioApiException.ParseErrors(errorContent), errorContent);
        }

        return response;
    }

    private static async Task<string> ReadAsync(HttpResponseMessage response, string relativeUrl, CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new MaxioApiException(response.StatusCode, new[] { $"Billing API returned an empty response for '{relativeUrl}'." });
        }

        return content;
    }
}