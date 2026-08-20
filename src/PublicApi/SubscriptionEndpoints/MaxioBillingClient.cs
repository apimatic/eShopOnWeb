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

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MaxioBillingClient : IMaxioBillingClient
{
    private const int PageSize = 200;
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly string _baseUrl;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    public MaxioBillingClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _baseUrl = string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? $"https://{_options.Subdomain}.chargify.com"
            : _options.BaseUrl;

        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ApiKey}:X"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("eShopOnWeb-Maxio/1.0");
    }

    public async Task<IReadOnlyList<BillingPlan>> GetPlansAsync(CancellationToken cancellationToken)
    {
        var plans = new List<BillingPlan>();
        var family = Uri.EscapeDataString(_options.ProductFamilyHandle);

        for (var page = 1; ; page++)
        {
            var path = $"product_families/handle:{family}/products.json?page={page}&per_page={PageSize}";
            var response = await SendAndReadAsync<List<ProductEnvelope>>(
                HttpMethod.Get,
                path,
                null,
                false,
                cancellationToken) ?? throw MalformedResponse();

            plans.AddRange(response
                .Select(x => x.Product)
                .Where(x => x.ArchivedAt == null &&
                            string.Equals(x.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.Ordinal))
                .Select(x => new BillingPlan(
                    Required(x.Handle, "product handle"),
                    x.Name,
                    x.Description,
                    x.PriceInCents,
                    x.Interval,
                    x.IntervalUnit,
                    x.RequireCreditCard)));

            if (response.Count < PageSize)
            {
                break;
            }
        }

        return plans;
    }

    public async Task<BillingCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        var response = await SendAndReadAsync<CustomerEnvelope>(
            HttpMethod.Get,
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}",
            null,
            true,
            cancellationToken);

        return response == null ? null : Map(response.Customer);
    }

    public async Task<BillingCustomer> CreateCustomerAsync(
        string reference,
        string firstName,
        string lastName,
        string email,
        CancellationToken cancellationToken)
    {
        var request = new
        {
            Customer = new
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference
            }
        };
        var response = await SendAndReadAsync<CustomerEnvelope>(
            HttpMethod.Post,
            "customers.json",
            request,
            false,
            cancellationToken) ?? throw MalformedResponse();

        return Map(response.Customer);
    }

    public async Task<IReadOnlyList<BillingSubscription>> GetCustomerSubscriptionsAsync(
        long customerId,
        CancellationToken cancellationToken)
    {
        var response = await SendAndReadAsync<List<SubscriptionEnvelope>>(
            HttpMethod.Get,
            $"customers/{customerId}/subscriptions.json",
            null,
            false,
            cancellationToken) ?? throw MalformedResponse();

        return response.Select(x => Map(x.Subscription)).ToList();
    }

    public async Task<BillingSubscription?> FindSubscriptionAsync(
        string reference,
        CancellationToken cancellationToken)
    {
        var response = await SendAndReadAsync<SubscriptionEnvelope>(
            HttpMethod.Get,
            $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}",
            null,
            true,
            cancellationToken);

        return response == null ? null : Map(response.Subscription);
    }

    public async Task<BillingSubscription> CreateSubscriptionAsync(
        long customerId,
        string productHandle,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        var request = new
        {
            Subscription = new
            {
                ProductHandle = productHandle,
                CustomerId = customerId,
                Reference = subscriptionReference,
                PaymentCollectionMethod = "remittance"
            }
        };
        var response = await SendAndReadAsync<SubscriptionEnvelope>(
            HttpMethod.Post,
            "subscriptions.json",
            request,
            false,
            cancellationToken) ?? throw MalformedResponse();

        return Map(response.Subscription);
    }

    private async Task<T?> SendAndReadAsync<T>(
        HttpMethod method,
        string relativePath,
        object? body,
        bool allowNotFound,
        CancellationToken cancellationToken)
        where T : class
    {
        const int maxGetAttempts = 3;
        var attempts = method == HttpMethod.Get ? maxGetAttempts : 1;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            using var request = new HttpRequestMessage(method, BuildUri(relativePath));
            if (body != null)
            {
                request.Content = JsonContent.Create(body, options: JsonOptions);
            }

            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (ShouldRetry(response.StatusCode) && attempt < attempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt), cancellationToken);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new MaxioApiException(
                    response.StatusCode,
                    await ReadErrorAsync(response, cancellationToken));
            }

            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        }

        throw new InvalidOperationException("Maxio request retry loop exited unexpectedly.");
    }

    private Uri BuildUri(string relativePath)
    {
        return new Uri($"{_baseUrl.TrimEnd('/')}/{relativePath.TrimStart('/')}", UriKind.Absolute);
    }

    private static bool ShouldRetry(HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        return statusCode == HttpStatusCode.TooManyRequests || code >= 500;
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var prefix = $"Maxio returned HTTP {(int)response.StatusCode}.";
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("errors", out var errors))
            {
                return prefix;
            }

            var details = errors.ValueKind switch
            {
                JsonValueKind.Array => string.Join(" ", errors.EnumerateArray().Select(x => x.ToString())),
                JsonValueKind.Object => string.Join(" ", errors.EnumerateObject().Select(x => $"{x.Name}: {x.Value}")),
                _ => errors.ToString()
            };
            return string.IsNullOrWhiteSpace(details) ? prefix : $"{prefix} {details}";
        }
        catch (JsonException)
        {
            return prefix;
        }
    }

    private static BillingCustomer Map(CustomerModel customer)
    {
        return new BillingCustomer(
            customer.Id,
            Required(customer.Reference, "customer reference"),
            customer.Email);
    }

    private static BillingSubscription Map(SubscriptionModel subscription)
    {
        var product = subscription.Product ?? throw MalformedResponse();
        var customer = subscription.Customer ?? throw MalformedResponse();
        return new BillingSubscription(
            subscription.Id,
            subscription.Reference,
            subscription.State,
            subscription.ProductPriceInCents,
            subscription.CurrentPeriodEndsAt,
            subscription.NextAssessmentAt,
            customer.Id,
            customer.Reference,
            Required(product.Handle, "product handle"),
            product.Name,
            product.Interval,
            product.IntervalUnit,
            Required(product.ProductFamily?.Handle, "product family handle"));
    }

    private static string Required(string? value, string field)
    {
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new MaxioApiException(HttpStatusCode.BadGateway, $"Maxio response omitted {field}.");
    }

    private static MaxioApiException MalformedResponse()
    {
        return new MaxioApiException(HttpStatusCode.BadGateway, "Maxio returned a malformed response.");
    }

    private sealed class ProductEnvelope
    {
        public ProductModel Product { get; set; } = new();
    }

    private sealed class CustomerEnvelope
    {
        public CustomerModel Customer { get; set; } = new();
    }

    private sealed class SubscriptionEnvelope
    {
        public SubscriptionModel Subscription { get; set; } = new();
    }

    private sealed class CustomerModel
    {
        public long Id { get; set; }
        public string? Reference { get; set; }
        public string Email { get; set; } = string.Empty;
    }

    private sealed class ProductFamilyModel
    {
        public string? Handle { get; set; }
    }

    private sealed class ProductModel
    {
        public string? Handle { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public long PriceInCents { get; set; }
        public int Interval { get; set; }
        public string IntervalUnit { get; set; } = string.Empty;
        public bool RequireCreditCard { get; set; }
        public DateTimeOffset? ArchivedAt { get; set; }
        public ProductFamilyModel? ProductFamily { get; set; }
    }

    private sealed class SubscriptionModel
    {
        public long Id { get; set; }
        public string? Reference { get; set; }
        public string State { get; set; } = string.Empty;
        public long ProductPriceInCents { get; set; }
        public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
        public DateTimeOffset? NextAssessmentAt { get; set; }
        public CustomerModel? Customer { get; set; }
        public ProductModel? Product { get; set; }
    }
}
