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
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public class MaxioAdvancedBillingClient : IMaxioAdvancedBillingClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
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

    public async Task<IReadOnlyList<SubscriptionPlan>> ListProductsForConfiguredFamilyAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        if (string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw new MaxioConfigurationException(
                "Maxio:ProductFamilyHandle is not configured. Set the MAXIO_DEFAULT_PRODUCT_FAMILY environment variable or the Maxio:ProductFamilyHandle user secret.");
        }

        var familyHandle = Uri.EscapeDataString(_options.ProductFamilyHandle);
        var payload = await SendAsync<List<ProductEnvelope>>(
            HttpMethod.Get,
            $"product_families/handle:{familyHandle}/products.json?per_page=200",
            body: null,
            uniquenessToken: null,
            cancellationToken);

        return (payload ?? new List<ProductEnvelope>())
            .Select(e => e.Product)
            .Where(p => p is not null && p.ArchivedAt is null && !string.IsNullOrWhiteSpace(p.Handle))
            .Select(p => p!.ToPlan())
            .ToList();
    }

    public async Task<BillingCustomer?> FindCustomerByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var envelope = await SendAsync<CustomerEnvelope>(
                HttpMethod.Get,
                $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}",
                body: null,
                uniquenessToken: null,
                cancellationToken);

            return envelope?.Customer?.ToBillingCustomer();
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<BillingCustomer> CreateCustomerAsync(
        NewBillingCustomer customer,
        CancellationToken cancellationToken = default)
    {
        var envelope = await SendAsync<CustomerEnvelope>(
            HttpMethod.Post,
            "customers.json",
            body: new
            {
                customer = new
                {
                    first_name = customer.FirstName,
                    last_name = customer.LastName,
                    email = customer.Email,
                    reference = customer.Reference
                }
            },
            uniquenessToken: Guid.NewGuid().ToString("N"),
            cancellationToken);

        if (envelope?.Customer is null)
        {
            throw new MaxioApiException(HttpStatusCode.BadGateway, "Maxio did not return a customer.");
        }

        return envelope.Customer.ToBillingCustomer();
    }

    public async Task<ShopperSubscription> CreateSubscriptionAsync(
        int customerId,
        string productHandle,
        string uniquenessToken,
        CancellationToken cancellationToken = default)
    {
        var envelope = await SendAsync<SubscriptionEnvelope>(
            HttpMethod.Post,
            "subscriptions.json",
            body: new
            {
                subscription = new
                {
                    product_handle = productHandle,
                    customer_id = customerId,
                    payment_collection_method = "remittance"
                }
            },
            uniquenessToken,
            cancellationToken);

        if (envelope?.Subscription is null)
        {
            throw new MaxioApiException(HttpStatusCode.BadGateway, "Maxio did not return a subscription.");
        }

        return envelope.Subscription.ToShopperSubscription();
    }

    public async Task<IReadOnlyList<ShopperSubscription>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken = default)
    {
        var payload = await SendAsync<List<SubscriptionEnvelope>>(
            HttpMethod.Get,
            $"customers/{customerId}/subscriptions.json?per_page=200",
            body: null,
            uniquenessToken: null,
            cancellationToken);

        return (payload ?? new List<SubscriptionEnvelope>())
            .Select(e => e.Subscription)
            .Where(s => s is not null)
            .Select(s => s!.ToShopperSubscription())
            .ToList();
    }

    private async Task<T?> SendAsync<T>(
        HttpMethod method,
        string relativePath,
        object? body,
        string? uniquenessToken,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();

        Exception? lastTransient = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var request = BuildRequest(method, relativePath, body, uniquenessToken);
            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, cancellationToken);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested && attempt < 2)
            {
                lastTransient = ex;
                await Task.Delay(TimeSpan.FromSeconds(2 * (attempt + 1)), cancellationToken);
                continue;
            }

            using (response)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);

                if ((int)response.StatusCode == 429 && attempt < 2)
                {
                    _logger.LogWarning("Maxio rate-limited the request; retrying after a short pause.");
                    await Task.Delay(TimeSpan.FromSeconds(2 * (attempt + 1)), cancellationToken);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new MaxioApiException(response.StatusCode, FormatError(response.StatusCode, content));
                }

                if (string.IsNullOrWhiteSpace(content) || content == "null")
                {
                    return default;
                }

                return JsonSerializer.Deserialize<T>(content, JsonOptions);
            }
        }

        throw lastTransient ?? new MaxioApiException(HttpStatusCode.ServiceUnavailable, "Maxio request failed after retries.");
    }

    private HttpRequestMessage BuildRequest(HttpMethod method, string relativePath, object? body, string? uniquenessToken)
    {
        var url = new Uri(new Uri(_options.ResolveBaseUrl(), UriKind.Absolute), relativePath);
        var request = new HttpRequestMessage(method, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ApiKey}:X")));

        if (body is not null || uniquenessToken is not null)
        {
            var node = JsonSerializer.SerializeToNode(body ?? new { }, JsonOptions)?.AsObject()
                ?? new System.Text.Json.Nodes.JsonObject();
            if (uniquenessToken is not null)
            {
                node["uniqueness_token"] = uniquenessToken;
            }

            request.Content = new StringContent(node.ToJsonString(), Encoding.UTF8, "application/json");
        }

        return request;
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new MaxioConfigurationException(
                "Maxio:ApiKey is not configured. Set the MAXIO_API_KEY environment variable or the Maxio:ApiKey user secret.");
        }

        if (string.IsNullOrWhiteSpace(_options.BaseUrl) && string.IsNullOrWhiteSpace(_options.Subdomain))
        {
            throw new MaxioConfigurationException(
                "Maxio:Subdomain or Maxio:BaseUrl must be configured.");
        }
    }

    private static string FormatError(HttpStatusCode statusCode, string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return $"Maxio request failed with {(int)statusCode} {statusCode}.";
        }

        try
        {
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.TryGetProperty("errors", out var errors))
            {
                if (errors.ValueKind == JsonValueKind.Array)
                {
                    var messages = errors.EnumerateArray()
                        .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : e.ToString())
                        .Where(m => !string.IsNullOrWhiteSpace(m));
                    var joined = string.Join(" ", messages);
                    if (!string.IsNullOrWhiteSpace(joined))
                    {
                        return joined;
                    }
                }
                else if (errors.ValueKind == JsonValueKind.String)
                {
                    return errors.GetString() ?? content;
                }
            }
        }
        catch (JsonException)
        {
            // Fall through to the raw body.
        }

        return content.Length > 1000 ? content[..1000] : content;
    }

    private sealed class CustomerEnvelope
    {
        public MaxioCustomerDto? Customer { get; set; }
    }

    private sealed class ProductEnvelope
    {
        public MaxioProductDto? Product { get; set; }
    }

    private sealed class SubscriptionEnvelope
    {
        public MaxioSubscriptionDto? Subscription { get; set; }
    }

    private sealed class MaxioCustomerDto
    {
        public int Id { get; set; }
        public string? Reference { get; set; }
        public string? Email { get; set; }

        public BillingCustomer ToBillingCustomer() =>
            new(Id, Reference, Email ?? string.Empty);
    }

    private sealed class MaxioProductDto
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Handle { get; set; }
        public string? Description { get; set; }
        public long PriceInCents { get; set; }
        public int Interval { get; set; }
        public string? IntervalUnit { get; set; }
        public DateTimeOffset? ArchivedAt { get; set; }

        public SubscriptionPlan ToPlan() =>
            new(
                Handle ?? string.Empty,
                Name ?? Handle ?? string.Empty,
                Description,
                PriceInCents / 100m,
                PriceInCents,
                Interval,
                IntervalUnit ?? "month");
    }

    private sealed class MaxioSubscriptionDto
    {
        public int Id { get; set; }
        public string? State { get; set; }
        public long ProductPriceInCents { get; set; }
        public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
        public DateTimeOffset? NextAssessmentAt { get; set; }
        public MaxioProductDto? Product { get; set; }

        public ShopperSubscription ToShopperSubscription()
        {
            var priceInCents = ProductPriceInCents != 0 ? ProductPriceInCents : Product?.PriceInCents ?? 0;
            return new ShopperSubscription(
                Id,
                State ?? string.Empty,
                Product?.Handle,
                Product?.Name,
                priceInCents / 100m,
                priceInCents,
                NextAssessmentAt ?? CurrentPeriodEndsAt);
        }
    }
}
