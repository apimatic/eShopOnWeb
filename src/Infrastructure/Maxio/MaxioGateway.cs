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
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Plain-HTTP client for the Maxio Advanced Billing REST API (formerly Chargify).
///
/// Contract notes, all confirmed against the official Maxio Advanced Billing
/// SDK (maxio-com/ab-python-sdk) and the live sandbox:
///  - Authentication is HTTP Basic with the site API key as username and "x" as password.
///  - US sites live at https://{subdomain}.chargify.com, EU sites at
///    https://{subdomain}.ebilling.maxio.com.
///  - List endpoints return JSON arrays of single-key envelope objects
///    ({"product": {...}}, {"subscription": {...}}, ...).
///  - A customer can be looked up by its unique external reference via
///    GET /customers/lookup.json?reference=... (404 when absent).
/// </summary>
public sealed class MaxioGateway : IMaxioGateway
{
    private const int PageSize = 200;
    private const string Password = "x";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioGateway> _logger;

    public MaxioGateway(HttpClient httpClient, IOptions<MaxioOptions> options, ILogger<MaxioGateway> logger)
    {
        var opts = options.Value;
        opts.Validate();

        _httpClient = httpClient;
        _logger = logger;

        if (_httpClient.BaseAddress is null)
        {
            _httpClient.BaseAddress = opts.ResolveBaseUrl();
        }

        var header = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{opts.ApiKey}:{Password}")));
        _httpClient.DefaultRequestHeaders.Authorization = header;
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<MaxioProductFamily?> FindProductFamilyByHandleAsync(string handle, CancellationToken ct)
    {
        var families = await GetPagedAsync("product_families.json", "product_family", ct);
        var match = families.FirstOrDefault(f =>
            string.Equals(f.GetProperty("handle").GetString(), handle, StringComparison.OrdinalIgnoreCase));
        return match.ValueKind == JsonValueKind.Undefined
            ? null
            : match.Deserialize<MaxioProductFamily>(SerializerOptions);
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsInFamilyAsync(int productFamilyId, CancellationToken ct)
    {
        var elements = await GetPagedAsync($"product_families/{productFamilyId}/products.json", "product", ct);
        return elements.Select(e => e.Deserialize<MaxioProduct>(SerializerOptions)!)
            .Where(p => p.ArchivedAt is null)
            .ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken ct)
    {
        var response = await SendAsync(HttpMethod.Get,
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, "customers/lookup.json");
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(SerializerOptions, ct);
        return payload.GetProperty("customer").Deserialize<MaxioCustomer>(SerializerOptions);
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(string reference, string firstName, string lastName,
        string email, CancellationToken ct)
    {
        var body = new
        {
            customer = new
            {
                first_name = firstName,
                last_name = lastName,
                email,
                reference
            }
        };

        var response = await SendAsync(HttpMethod.Post, "customers.json", ct, body);
        await EnsureSuccessAsync(response, "customers.json");
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(SerializerOptions, ct);
        return payload.GetProperty("customer").Deserialize<MaxioCustomer>(SerializerOptions)!;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, int productId, CancellationToken ct)
    {
        var body = new
        {
            subscription = new
            {
                product_id = productId,
                customer_id = customerId,
                // The seeded plans do not require a card, but the site still
                // refuses signups without a payment method unless the
                // collection method is invoice-based. Verified live.
                payment_collection_method = "invoice"
            }
        };

        var response = await SendAsync(HttpMethod.Post, "subscriptions.json", ct, body);
        await EnsureSuccessAsync(response, "subscriptions.json");
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(SerializerOptions, ct);
        return payload.GetProperty("subscription").Deserialize<MaxioSubscription>(SerializerOptions)!;
    }

    public async Task<MaxioSubscription?> GetSubscriptionAsync(int subscriptionId, CancellationToken ct)
    {
        var response = await SendAsync(HttpMethod.Get, $"subscriptions/{subscriptionId}.json", ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, $"subscriptions/{subscriptionId}.json");
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(SerializerOptions, ct);
        return payload.GetProperty("subscription").Deserialize<MaxioSubscription>(SerializerOptions);
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsForCustomerAsync(int customerId, CancellationToken ct)
    {
        var elements = await GetPagedAsync($"subscriptions.json?customer_id={customerId}", "subscription", ct);
        return elements.Select(e => e.Deserialize<MaxioSubscription>(SerializerOptions)!).ToList();
    }

    /// <summary>
    /// Pages through a Maxio list endpoint, unwrapping the single-key envelope
    /// around each item. Stops when a page comes back short.
    /// </summary>
    private async Task<List<JsonElement>> GetPagedAsync(string path, string envelopeKey, CancellationToken ct)
    {
        var items = new List<JsonElement>();
        var page = 1;
        while (true)
        {
            var separator = path.Contains('?') ? '&' : '?';
            var response = await SendAsync(HttpMethod.Get, $"{path}{separator}page={page}&per_page={PageSize}", ct);
            await EnsureSuccessAsync(response, path);

            var batch = await response.Content.ReadFromJsonAsync<List<JsonElement>>(SerializerOptions, ct) ?? new();
            items.AddRange(batch
                .Where(e => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(envelopeKey, out _))
                .Select(e => e.GetProperty(envelopeKey)));

            if (batch.Count < PageSize)
            {
                return items;
            }

            page++;
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, CancellationToken ct,
        object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        _logger.LogDebug("Maxio {Method} {Path}", method.Method, path);
        try
        {
            return await _httpClient.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new MaxioUnavailableException($"Maxio Advanced Billing is unreachable ({method.Method} {path}).",
                innerException: ex);
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string path)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var errors = await ParseErrorsAsync(response);
        var message = $"Maxio API call to {path} failed with HTTP {(int)response.StatusCode}" +
                      (errors.Count > 0 ? $": {string.Join("; ", errors)}" : ".");

        throw response.StatusCode switch
        {
            HttpStatusCode.NotFound => new MaxioNotFoundException(message),
            HttpStatusCode.Conflict => new MaxioConflictException(message),
            HttpStatusCode.UnprocessableEntity => new MaxioApiException(422, errors, path),
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new MaxioUnavailableException(message,
                (int)response.StatusCode),
            _ => new MaxioUnavailableException(message, (int)response.StatusCode)
        };
    }

    private static async Task<IReadOnlyList<string>> ParseErrorsAsync(HttpResponseMessage response)
    {
        try
        {
            var payload = await response.Content.ReadFromJsonAsync<JsonElement>(SerializerOptions);
            if (!payload.TryGetProperty("errors", out var errors))
            {
                return new List<string>();
            }

            return errors.ValueKind switch
            {
                JsonValueKind.Array => errors.EnumerateArray()
                    .Select(e => e.GetString() ?? e.GetRawText())
                    .ToList(),
                JsonValueKind.Object => errors.EnumerateObject()
                    .SelectMany(p => p.Value.ValueKind == JsonValueKind.Array
                        ? p.Value.EnumerateArray().Select(e => $"{p.Name}: {e.GetString()}")
                        : new[] { $"{p.Name}: {p.Value.GetRawText()}" })
                    .ToList(),
                JsonValueKind.String => new List<string> { errors.GetString()! },
                _ => new List<string>()
            };
        }
        catch
        {
            return new List<string>();
        }
    }
}
