using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Low-level typed client for the Maxio Advanced Billing REST API.
/// </summary>
public interface IMaxioApiClient
{
    Task<IReadOnlyList<MaxioApiProduct>> ListProductsForFamilyAsync(string familyHandleOrId, CancellationToken cancellationToken);
    Task<MaxioApiCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);
    Task<MaxioApiCustomer> CreateCustomerAsync(string reference, string firstName, string lastName, string email, CancellationToken cancellationToken);
    Task<IReadOnlyList<MaxioApiSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken);
    Task<MaxioApiSubscription> CreateSubscriptionAsync(string productHandle, string customerReference, string paymentCollectionMethod, CancellationToken cancellationToken);
}

// ---------------------------------------------------------------------------
// Wire models (snake_case on the wire)
// ---------------------------------------------------------------------------

public sealed class MaxioApiCustomer
{
    public long Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
    public string? Organization { get; set; }
}

public sealed class MaxioApiProductFamily
{
    public long Id { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
}

public sealed class MaxioApiProduct
{
    public long Id { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public string? ArchivedAt { get; set; }
    public bool RequireCreditCard { get; set; }
    public MaxioApiProductFamily? ProductFamily { get; set; }
}

public sealed class MaxioApiSubscription
{
    public long Id { get; set; }
    public string? State { get; set; }
    public MaxioApiProduct? Product { get; set; }
    public long? ProductPriceInCents { get; set; }
    public string? CurrentPeriodEndsAt { get; set; }
    public string? NextAssessmentAt { get; set; }
    public string? ActivatedAt { get; set; }
    public string? CreatedAt { get; set; }
    public string? CanceledAt { get; set; }
    public string? ExpiresAt { get; set; }
    public bool? CancelAtEndOfPeriod { get; set; }
    public MaxioApiCustomer? Customer { get; set; }
}

public sealed class MaxioApiProductEnvelope
{
    public MaxioApiProduct? Product { get; set; }
}

public sealed class MaxioApiCustomerEnvelope
{
    public MaxioApiCustomer? Customer { get; set; }
}

public sealed class MaxioApiCreateCustomerEnvelope
{
    public MaxioApiCustomerAttributes Customer { get; set; } = new();
}

public sealed class MaxioApiCustomerAttributes
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}

public sealed class MaxioApiCreateSubscriptionEnvelope
{
    public MaxioApiCreateSubscriptionAttributes Subscription { get; set; } = new();
}

public sealed class MaxioApiCreateSubscriptionAttributes
{
    public string? ProductHandle { get; set; }
    public string? CustomerReference { get; set; }
    public string? PaymentCollectionMethod { get; set; }
}

public sealed class MaxioApiSubscriptionEnvelope
{
    public MaxioApiSubscription? Subscription { get; set; }
}

// ---------------------------------------------------------------------------
// Client implementation
// ---------------------------------------------------------------------------

public sealed class MaxioApiClient : IMaxioApiClient
{
    private const int MaxPageSize = 200;
    private const string BasicPassword = "X";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;

    public MaxioApiClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

public async Task<IReadOnlyList<MaxioApiProduct>> ListProductsForFamilyAsync(string familyHandleOrId, CancellationToken cancellationToken)
    {
        var products = new List<MaxioApiProduct>();
        for (var page = 1; ; page++)
        {
            // The path parameter accepts either a numeric id or "handle:<handle>".
            var familyPath = familyHandleOrId.All(char.IsDigit)
                ? familyHandleOrId
                : $"handle:{Uri.EscapeDataString(familyHandleOrId)}";
            var url = $"product_families/{familyPath}/products.json?page={page}&per_page={MaxPageSize}";
            var pageProducts = await GetAsync<List<MaxioApiProductEnvelope>>(url, cancellationToken);
            if (pageProducts is null || pageProducts.Count == 0)
            {
                break;
            }

            products.AddRange(pageProducts.Where(w => w.Product is not null).Select(w => w.Product!));
            if (pageProducts.Count < MaxPageSize)
            {
                break;
            }
        }

        return products;
    }

    public async Task<MaxioApiCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var envelope = await GetAsync<MaxioApiCustomerEnvelope>(
                $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
            return envelope?.Customer;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<MaxioApiCustomer> CreateCustomerAsync(string reference, string firstName, string lastName, string email, CancellationToken cancellationToken)
    {
        var payload = new MaxioApiCreateCustomerEnvelope
        {
            Customer = new MaxioApiCustomerAttributes
            {
                Reference = reference,
                FirstName = firstName,
                LastName = lastName,
                Email = email
            }
        };

        var envelope = await PostAsync<MaxioApiCreateCustomerEnvelope, MaxioApiCustomerEnvelope>("customers.json", payload, cancellationToken);
        return envelope!.Customer!;
    }

    public async Task<IReadOnlyList<MaxioApiSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        var envelopes = await GetAsync<List<MaxioApiSubscriptionEnvelope>>($"customers/{customerId}/subscriptions.json", cancellationToken);
        return envelopes?.Where(w => w.Subscription is not null).Select(w => w.Subscription!).ToList()
               ?? new List<MaxioApiSubscription>();
    }

public async Task<MaxioApiSubscription> CreateSubscriptionAsync(string productHandle, string customerReference, string paymentCollectionMethod, CancellationToken cancellationToken)
    {
        var payload = new MaxioApiCreateSubscriptionEnvelope
        {
            Subscription = new MaxioApiCreateSubscriptionAttributes
            {
                ProductHandle = productHandle,
                CustomerReference = customerReference,
                PaymentCollectionMethod = paymentCollectionMethod
            }
        };

        var envelope = await PostAsync<MaxioApiCreateSubscriptionEnvelope, MaxioApiSubscriptionEnvelope>("subscriptions.json", payload, cancellationToken);
        return envelope!.Subscription!;
    }

    private async Task<T?> GetAsync<T>(string relativeUrl, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        using var request = new HttpRequestMessage(HttpMethod.Get, relativeUrl);
        return await SendAsync<T>(request, cancellationToken);
    }

    private async Task<TResponse?> PostAsync<TBody, TResponse>(string relativeUrl, TBody body, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        using var request = new HttpRequestMessage(HttpMethod.Post, relativeUrl)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, SerializerOptions), Encoding.UTF8, "application/json")
        };
        return await SendAsync<TResponse>(request, cancellationToken);
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException("Maxio:ApiKey is not configured. Set the MAXIO_API_KEY environment variable or a matching user-secret.");
        }

        if (_httpClient.BaseAddress is null)
        {
            throw new InvalidOperationException("The Maxio API base address is not configured. Set Maxio:Subdomain or Maxio:BaseUrl.");
        }
    }

    private async Task<T?> SendAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new MaxioApiException(request.RequestUri?.ToString() ?? request.Method.Method, response.StatusCode, body);
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(body, SerializerOptions);
    }
}

/// <summary>
/// Configures the typed Maxio HTTP client (base address + Basic auth).
/// </summary>
internal static class MaxioHttpClientSetup
{
    public static void Configure(HttpClient httpClient, MaxioOptions options)
    {
        var baseAddress = options.ResolveBaseUrl();
        httpClient.BaseAddress = new Uri(baseAddress, UriKind.Absolute);
        httpClient.Timeout = TimeSpan.FromSeconds(30);

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ApiKey}:X"));
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }
}
