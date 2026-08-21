using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// HTTP client for Maxio Advanced Billing. Authentication is HTTP Basic with the
/// API key as username and <c>x</c> as password (Maxio Core Resources for Building
/// an Integration). Resource paths are those in ab-dotnet-sdk 9.1.0 controllers.
/// </summary>
public class MaxioAdvancedBillingClient : IMaxioAdvancedBillingClient
{
    private static readonly TimeSpan[] RetryDelays =
    {
        TimeSpan.FromMilliseconds(200),
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(1)
    };

    private readonly HttpClient _http;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioAdvancedBillingClient> _logger;

    public MaxioAdvancedBillingClient(
        HttpClient http,
        IOptions<MaxioOptions> options,
        ILogger<MaxioAdvancedBillingClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public string ProductFamilyHandle => _options.ProductFamilyHandle;

    public async Task<IReadOnlyList<BillingProduct>> ListFamilyProductsAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        // GET /product_families/{product_family_id}/products.json
        // product_family_id may be the numeric id or `handle:{handle}`.
        var familyId = $"handle:{_options.ProductFamilyHandle}";
        var path = $"/product_families/{familyId}/products.json?per_page=200&include_archived=false";
        var envelopes = await GetJsonAsync<List<MaxioProductEnvelope>>(path, cancellationToken);
        return (envelopes ?? new List<MaxioProductEnvelope>())
            .Select(e => e.Product?.ToDomain())
            .Where(p => p is not null && !p.IsArchived)
            .Select(p => p!)
            .ToList();
    }

    public async Task<BillingProduct?> GetProductByHandleAsync(string handle, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        // GET /products/handle/{apiHandle}.json
        var path = $"/products/handle/{Uri.EscapeDataString(handle)}.json";
        var response = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Get, path), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response);
        var envelope = await ReadJsonAsync<MaxioProductEnvelope>(response);
        return envelope?.Product?.ToDomain();
    }

    public async Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        // GET /customers/lookup.json?reference={reference}
        var path = $"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var response = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Get, path), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response);
        var envelope = await ReadJsonAsync<MaxioCustomerEnvelope>(response);
        return envelope?.Customer?.ToDomain();
    }

    public async Task<BillingCustomer> CreateCustomerAsync(
        string firstName,
        string lastName,
        string email,
        string reference,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        // POST /customers.json  (documented 200 OK)
        var body = new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference
            }
        };

        var response = await SendOnceAsync(
            () => JsonPost("/customers.json", body),
            cancellationToken);
        await EnsureSuccessAsync(response, allowStatuses: new[] { HttpStatusCode.OK, HttpStatusCode.Created });
        var envelope = await ReadJsonAsync<MaxioCustomerEnvelope>(response);
        if (envelope?.Customer is null)
        {
            throw new BillingException(502, "Maxio created a customer but returned an empty body.");
        }

        return envelope.Customer.ToDomain();
    }

    public async Task<IReadOnlyList<BillingSubscription>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        // GET /customers/{customer_id}/subscriptions.json
        var path = $"/customers/{customerId}/subscriptions.json";
        var envelopes = await GetJsonAsync<List<MaxioSubscriptionEnvelope>>(path, cancellationToken);
        return (envelopes ?? new List<MaxioSubscriptionEnvelope>())
            .Select(e => e.Subscription?.ToDomain())
            .Where(s => s is not null)
            .Select(s => s!)
            .ToList();
    }

    public async Task<BillingSubscription?> FindSubscriptionByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        // GET /subscriptions/lookup.json?reference={reference}
        var path = $"/subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var response = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Get, path), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response);
        var envelope = await ReadJsonAsync<MaxioSubscriptionEnvelope>(response);
        return envelope?.Subscription?.ToDomain();
    }

    public async Task<BillingSubscription> CreateSubscriptionAsync(
        int customerId,
        string productHandle,
        string reference,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        // POST /subscriptions.json  (documented 201 Created)
        var body = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscription
            {
                CustomerId = customerId,
                ProductHandle = productHandle,
                Reference = reference
            }
        };

        var response = await SendOnceAsync(
            () => JsonPost("/subscriptions.json", body),
            cancellationToken);
        await EnsureSuccessAsync(response, allowStatuses: new[] { HttpStatusCode.OK, HttpStatusCode.Created });
        var envelope = await ReadJsonAsync<MaxioSubscriptionEnvelope>(response);
        if (envelope?.Subscription is null)
        {
            throw new BillingException(502, "Maxio created a subscription but returned an empty body.");
        }

        return envelope.Subscription.ToDomain();
    }

    private void EnsureConfigured()
    {
        if (!_options.IsConfigured)
        {
            throw new BillingException(503, "Maxio billing is not configured. Set Maxio:ApiKey, Maxio:Subdomain (or Maxio:BaseUrl), and Maxio:ProductFamilyHandle.");
        }
    }

    private HttpRequestMessage JsonPost<T>(string path, T body)
    {
        var json = JsonSerializer.Serialize(body, MaxioJson.Options);
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        return request;
    }

    private async Task<T?> GetJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        var response = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Get, path), cancellationToken);
        await EnsureSuccessAsync(response);
        return await ReadJsonAsync<T>(response);
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(json))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(json, MaxioJson.Options);
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(
        Func<HttpRequestMessage> createRequest,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage? last = null;
        for (var attempt = 0; attempt <= RetryDelays.Length; attempt++)
        {
            last?.Dispose();
            last = await _http.SendAsync(createRequest(), cancellationToken);
            if (!IsTransient(last.StatusCode) || attempt == RetryDelays.Length)
            {
                return last;
            }

            _logger.LogWarning("Transient Maxio HTTP {Status} on {Method} {Uri}; retrying.", (int)last.StatusCode, last.RequestMessage?.Method, last.RequestMessage?.RequestUri);
            await Task.Delay(RetryDelays[attempt], cancellationToken);
        }

        return last!;
    }

    private Task<HttpResponseMessage> SendOnceAsync(
        Func<HttpRequestMessage> createRequest,
        CancellationToken cancellationToken)
        => _http.SendAsync(createRequest(), cancellationToken);

    private static bool IsTransient(HttpStatusCode status) =>
        status == HttpStatusCode.TooManyRequests
        || status == HttpStatusCode.BadGateway
        || status == HttpStatusCode.ServiceUnavailable
        || status == HttpStatusCode.GatewayTimeout
        || (int)status == 408;

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        HttpStatusCode[]? allowStatuses = null)
    {
        if (allowStatuses is not null)
        {
            if (allowStatuses.Contains(response.StatusCode))
            {
                return;
            }
        }
        else if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync();
        var maxioStatus = (int)response.StatusCode;
        var detail = TryFormatMaxioError(body) ?? $"Maxio request failed with HTTP {maxioStatus}.";
        throw new BillingException(MapStatus(maxioStatus), detail);
    }

    private static int MapStatus(int maxioStatus) => maxioStatus switch
    {
        401 or 403 => 503,
        >= 500 => 502,
        422 => 422,
        404 => 404,
        _ => maxioStatus
    };

    private static string? TryFormatMaxioError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("errors", out var errors))
            {
                return body.Length > 500 ? body[..500] : body;
            }

            if (errors.ValueKind == JsonValueKind.Array)
            {
                var messages = errors.EnumerateArray()
                    .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : e.ToString())
                    .Where(s => !string.IsNullOrWhiteSpace(s));
                return string.Join(" ", messages!);
            }

            if (errors.ValueKind == JsonValueKind.Object)
            {
                var messages = errors.EnumerateObject()
                    .Select(p => $"{p.Name}: {p.Value}");
                return string.Join(" ", messages);
            }

            if (errors.ValueKind == JsonValueKind.String)
            {
                return errors.GetString();
            }
        }
        catch (JsonException)
        {
            return body.Length > 500 ? body[..500] : body;
        }

        return null;
    }
}
