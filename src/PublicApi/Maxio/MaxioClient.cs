using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Plain-HTTP client for the Maxio Advanced Billing API (Chargify-compatible REST API).
/// Routes, auth scheme (Basic: API key as username, "x" as password) and payload shapes
/// were verified against a live Advanced Billing sandbox. No SDK dependency is used.
/// </summary>
public class MaxioClient : IMaxioClient
{
    private const int ProductsPageSize = 100;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;

    public MaxioClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    private sealed record MaxioRawResponse(int StatusCode, string Body);

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken)
    {
        var products = new List<MaxioProduct>();
        var page = 1;
        int totalPages;

        do
        {
            var raw = await SendAsync(HttpMethod.Get, $"products.json?per_page={ProductsPageSize}&page={page}", allowNotFound: false, cancellationToken);
            (var pageProducts, totalPages) = ParseProductsPage(raw!.Body);
            products.AddRange(pageProducts);
            page++;
        }
        while (page <= totalPages);

        return products.Where(p => !string.IsNullOrEmpty(p.Handle)).ToList();
    }

    public async Task<MaxioProduct?> GetProductByHandleAsync(string handle, CancellationToken cancellationToken)
    {
        // GET /products/handle/{handle}.json — product lookup by stable handle.
        var raw = await SendAsync(HttpMethod.Get, $"products/handle/{Uri.EscapeDataString(handle)}.json", allowNotFound: true, cancellationToken);
        return raw is null ? null : JsonSerializer.Deserialize<MaxioProduct>(ExtractWrapper(raw.Body, "product"), JsonOptions);
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        // GET /customers/lookup.json?reference={reference} — customer lookup by app-side reference.
        var raw = await SendAsync(HttpMethod.Get, $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", allowNotFound: true, cancellationToken);
        return raw is null ? null : JsonSerializer.Deserialize<MaxioCustomer>(ExtractWrapper(raw.Body, "customer"), JsonOptions);
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(string reference, string firstName, string lastName, string email, CancellationToken cancellationToken)
    {
        var payload = new
        {
            customer = new
            {
                reference,
                first_name = firstName,
                last_name = lastName,
                email
            }
        };

        var raw = await SendJsonAsync(HttpMethod.Post, "customers.json", payload, cancellationToken);
        return JsonSerializer.Deserialize<MaxioCustomer>(ExtractWrapper(raw.Body, "customer"), JsonOptions)!;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        var raw = await SendAsync(HttpMethod.Get, $"customers/{customerId}/subscriptions.json", allowNotFound: false, cancellationToken);
        return ExtractList<MaxioSubscription>(raw!.Body, "subscription");
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(long customerId, string productHandle, CancellationToken cancellationToken)
    {
        // "payment_collection_method": "invoice" enrolls without card capture / 3-DS
        // (verified live: omitting it yields 422 "No payment method was on file...").
        var payload = new
        {
            subscription = new
            {
                customer_id = customerId,
                product_handle = productHandle,
                payment_collection_method = "invoice"
            }
        };

        var raw = await SendJsonAsync(HttpMethod.Post, "subscriptions.json", payload, cancellationToken);
        return JsonSerializer.Deserialize<MaxioSubscription>(ExtractWrapper(raw.Body, "subscription"), JsonOptions)!;
    }

    private async Task<MaxioRawResponse?> SendAsync(HttpMethod method, string path, bool allowNotFound, CancellationToken cancellationToken, string? jsonBody = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (jsonBody is not null)
        {
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (allowNotFound && response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new MaxioApiException((int)response.StatusCode, path, body);
        }

        return new MaxioRawResponse((int)response.StatusCode, body);
    }

    private async Task<MaxioRawResponse> SendJsonAsync(HttpMethod method, string path, object payload, CancellationToken cancellationToken)
    {
        return (await SendAsync(method, path, allowNotFound: false, cancellationToken, JsonSerializer.Serialize(payload)))!;
    }

    private static (IReadOnlyList<MaxioProduct> products, int totalPages) ParseProductsPage(string body)
    {
        var products = new List<MaxioProduct>();
        var totalPages = 1;

        using var document = JsonDocument.Parse(body);
        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (element.TryGetProperty("product", out var productElement))
            {
                var product = JsonSerializer.Deserialize<MaxioProduct>(productElement.GetRawText(), JsonOptions);
                if (product is not null)
                {
                    products.Add(product);
                }
            }
            else if (element.TryGetProperty("meta", out var metaElement) &&
                     metaElement.TryGetProperty("total_pages", out var totalPagesElement))
            {
                totalPages = Math.Max(totalPages, totalPagesElement.GetInt32());
            }
        }

        return (products, totalPages);
    }

    private static string ExtractWrapper(string body, string wrapper)
    {
        using var document = JsonDocument.Parse(body);

        if (!document.RootElement.TryGetProperty(wrapper, out var element))
        {
            throw new MaxioApiException(0, wrapper, $"Unexpected Maxio response shape: missing '{wrapper}' in {body}");
        }

        return element.GetRawText();
    }

    private static List<T> ExtractList<T>(string body, string wrapper)
    {
        var items = new List<T>();
        using var document = JsonDocument.Parse(body);

        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (element.TryGetProperty(wrapper, out var inner))
            {
                var item = JsonSerializer.Deserialize<T>(inner.GetRawText(), JsonOptions);
                if (item is not null)
                {
                    items.Add(item);
                }
            }
        }

        return items;
    }
}
