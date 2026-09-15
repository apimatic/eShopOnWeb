using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// HTTP client for Maxio Advanced Billing (formerly Chargify).
/// Auth is HTTP Basic with the API key as username and <c>x</c> as password.
/// </summary>
public sealed class MaxioAdvancedBillingClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private bool _httpClientConfigured;

    public MaxioAdvancedBillingClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
        TryConfigureHttpClient();
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException("Maxio:ApiKey is not configured. Set user-secrets or the MAXIO_API_KEY environment variable.");
        }

        if (string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw new InvalidOperationException("Maxio:ProductFamilyHandle is not configured. Set user-secrets or the MAXIO_DEFAULT_PRODUCT_FAMILY environment variable.");
        }

        if (string.IsNullOrWhiteSpace(_options.BaseUrl) && string.IsNullOrWhiteSpace(_options.Subdomain))
        {
            throw new InvalidOperationException("Maxio:Subdomain or Maxio:BaseUrl is required.");
        }

        TryConfigureHttpClient();
    }

    private void TryConfigureHttpClient()
    {
        if (_httpClientConfigured)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.BaseUrl) && string.IsNullOrWhiteSpace(_options.Subdomain))
        {
            return;
        }

        _httpClient.BaseAddress = new Uri(_options.ResolveBaseUrl());
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        _httpClient.DefaultRequestHeaders.Accept.Clear();
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        _httpClientConfigured = true;
    }

    public string ProductFamilyHandle => _options.ProductFamilyHandle;

    public async Task<IReadOnlyList<SubscriptionPlan>> ListProductsForFamilyAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken)
    {
        // Handle-prefixed family id is documented by Maxio: GET /product_families/{id|handle:xxx}/products.json
        EnsureConfigured();
        var path = $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json?per_page=200";
        var wrappers = await SendAsync<List<MaxioProductResponse>>(HttpMethod.Get, path, null, cancellationToken)
                       ?? new List<MaxioProductResponse>();

        return wrappers
            .Select(w => w.Product)
            .Where(p => p is not null && p.ArchivedAt is null)
            .Select(p => p!.ToPlan())
            .ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var response = await SendAsync<MaxioCustomerResponse>(HttpMethod.Get, path, null, cancellationToken, allowNotFound: true);
        return response?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(BillingShopper shopper, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var body = new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCreateCustomer
            {
                FirstName = shopper.FirstName,
                LastName = shopper.LastName,
                Email = shopper.Email,
                Reference = shopper.CustomerReference
            }
        };

        var response = await SendAsync<MaxioCustomerResponse>(HttpMethod.Post, "customers.json", body, cancellationToken);
        if (response?.Customer is null)
        {
            throw new BillingException("Maxio created a customer but returned an empty body.", 502);
        }

        return response.Customer;
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListCustomerSubscriptionsAsync(
        long customerId,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var path = $"customers/{customerId}/subscriptions.json";
        var wrappers = await SendAsync<List<MaxioSubscriptionResponse>>(HttpMethod.Get, path, null, cancellationToken)
                       ?? new List<MaxioSubscriptionResponse>();

        return wrappers
            .Select(w => w.Subscription)
            .Where(s => s is not null)
            .Select(s => s!.ToSubscription())
            .ToList();
    }

    public async Task<CustomerSubscription> CreateSubscriptionAsync(
        long customerId,
        string productHandle,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        // payment_collection_method=remittance is the Relationship Invoicing option for
        // enrollments that do not collect a card (products are configured require_credit_card=false).
        var body = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscription
            {
                ProductHandle = productHandle,
                CustomerId = customerId,
                PaymentCollectionMethod = "remittance"
            }
        };

        var response = await SendAsync<MaxioSubscriptionResponse>(HttpMethod.Post, "subscriptions.json", body, cancellationToken);
        if (response?.Subscription is null)
        {
            throw new BillingException("Maxio created a subscription but returned an empty body.", 502);
        }

        return response.Subscription.ToSubscription();
    }

    private async Task<T?> SendAsync<T>(
        HttpMethod method,
        string relativePath,
        object? body,
        CancellationToken cancellationToken,
        bool allowNotFound = false)
    {
        using var request = new HttpRequestMessage(method, relativePath);
        if (body is not null)
        {
            request.Content = new StringContent(
                JsonSerializer.Serialize(body, JsonOptions),
                Encoding.UTF8,
                "application/json");
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new BillingException(
                FormatMaxioError(response.StatusCode, payload),
                MapStatusCode(response.StatusCode));
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(payload, JsonOptions);
    }

    private static int MapStatusCode(HttpStatusCode statusCode) =>
        statusCode switch
        {
            HttpStatusCode.BadRequest => 400,
            HttpStatusCode.Unauthorized => 502,
            HttpStatusCode.Forbidden => 502,
            HttpStatusCode.NotFound => 404,
            HttpStatusCode.UnprocessableEntity => 400,
            HttpStatusCode.Conflict => 409,
            HttpStatusCode.TooManyRequests => 429,
            _ => 502
        };

    private static string FormatMaxioError(HttpStatusCode statusCode, string payload)
    {
        var detail = ExtractErrorDetail(payload);
        return string.IsNullOrWhiteSpace(detail)
            ? $"Maxio Advanced Billing returned {(int)statusCode} {statusCode}."
            : detail;
    }

    private static string ExtractErrorDetail(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            if (!document.RootElement.TryGetProperty("errors", out var errors))
            {
                return payload.Length > 500 ? payload[..500] : payload;
            }

            if (errors.ValueKind == JsonValueKind.Array)
            {
                return string.Join(" ", errors.EnumerateArray().Select(e => e.ToString()));
            }

            if (errors.ValueKind == JsonValueKind.Object)
            {
                var parts = new List<string>();
                foreach (var property in errors.EnumerateObject())
                {
                    parts.Add($"{property.Name}: {property.Value}");
                }

                return string.Join(" ", parts);
            }

            return errors.ToString();
        }
        catch (JsonException)
        {
            return payload.Length > 500 ? payload[..500] : payload;
        }
    }
}

internal sealed class MaxioProductResponse
{
    public MaxioProduct? Product { get; set; }
}

internal sealed class MaxioProduct
{
    public long Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public DateTimeOffset? ArchivedAt { get; set; }

    public SubscriptionPlan ToPlan() => new(
        Handle,
        Name,
        Description,
        PriceInCents / 100m,
        Interval,
        IntervalUnit);
}

internal sealed class MaxioCustomerResponse
{
    public MaxioCustomer? Customer { get; set; }
}

public sealed class MaxioCustomer
{
    public long Id { get; set; }
    public string? Reference { get; set; }
    public string Email { get; set; } = string.Empty;
}

internal sealed class MaxioCreateCustomerRequest
{
    public MaxioCreateCustomer Customer { get; set; } = new();
}

internal sealed class MaxioCreateCustomer
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
}

internal sealed class MaxioSubscriptionResponse
{
    public MaxioSubscription? Subscription { get; set; }
}

internal sealed class MaxioSubscription
{
    public long Id { get; set; }
    public string State { get; set; } = string.Empty;
    public int ProductPriceInCents { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public MaxioProduct? Product { get; set; }

    public CustomerSubscription ToSubscription() => new(
        Id,
        State,
        Product?.Handle ?? string.Empty,
        Product?.Name ?? string.Empty,
        ProductPriceInCents / 100m,
        NextAssessmentAt);
}

internal sealed class MaxioCreateSubscriptionRequest
{
    public MaxioCreateSubscription Subscription { get; set; } = new();
}

internal sealed class MaxioCreateSubscription
{
    public string ProductHandle { get; set; } = string.Empty;
    public long CustomerId { get; set; }
    public string PaymentCollectionMethod { get; set; } = string.Empty;
}
