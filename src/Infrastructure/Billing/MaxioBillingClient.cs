using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public class MaxioBillingClient : IMaxioBillingClient
{
    private const int MaxPageSize = 200;
    private const int MaxRetriesOnTooManyRequests = 3;

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioBillingClient> _logger;

    public MaxioBillingClient(
        HttpClient httpClient,
        IOptions<MaxioOptions> options,
        ILogger<MaxioBillingClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        ConfigureHttpClient();
    }

    public async Task<IReadOnlyList<BillingProduct>> ListProductsForFamilyAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productFamilyHandle);

        var products = new List<BillingProduct>();
        var page = 1;
        while (true)
        {
            var path =
                $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json?page={page}&per_page={MaxPageSize}";
            var pageItems = await SendAsync<List<ProductEnvelope>>(HttpMethod.Get, path, null, cancellationToken)
                            ?? new List<ProductEnvelope>();

            foreach (var envelope in pageItems)
            {
                if (envelope.Product is null || string.IsNullOrWhiteSpace(envelope.Product.Handle))
                {
                    continue;
                }

                products.Add(MapProduct(envelope.Product));
            }

            if (pageItems.Count < MaxPageSize)
            {
                break;
            }

            page++;
        }

        return products;
    }

    public async Task<BillingCustomer?> FindCustomerByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);

        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var envelope = await SendAsync<CustomerEnvelope>(
            HttpMethod.Get, path, null, cancellationToken, treatNotFoundAsEmpty: true);
        return envelope?.Customer is null ? null : MapCustomer(envelope.Customer);
    }

    public async Task<BillingCustomer> CreateCustomerAsync(
        BillingCustomerDraft customer,
        string uniquenessToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(customer);

        var body = new CreateCustomerRequestBody
        {
            UniquenessToken = uniquenessToken,
            Customer = new CreateCustomerPayload
            {
                FirstName = customer.FirstName,
                LastName = customer.LastName,
                Email = customer.Email,
                Reference = customer.Reference
            }
        };

        var envelope = await SendAsync<CustomerEnvelope>(
            HttpMethod.Post, "customers.json", body, cancellationToken);
        if (envelope?.Customer is null)
        {
            throw new MaxioBillingException(
                HttpStatusCode.BadGateway,
                "Maxio did not return a customer payload.");
        }

        return MapCustomer(envelope.Customer);
    }

    public async Task<IReadOnlyList<BillingSubscription>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken = default)
    {
        var path = $"customers/{customerId}/subscriptions.json";
        var pageItems = await SendAsync<List<SubscriptionEnvelope>>(
            HttpMethod.Get, path, null, cancellationToken) ?? new List<SubscriptionEnvelope>();

        return pageItems
            .Where(item => item.Subscription is not null)
            .Select(item => MapSubscription(item.Subscription!))
            .ToList();
    }

    public async Task<BillingSubscription?> FindSubscriptionByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);

        var path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var envelope = await SendAsync<SubscriptionEnvelope>(
            HttpMethod.Get, path, null, cancellationToken, treatNotFoundAsEmpty: true);
        return envelope?.Subscription is null ? null : MapSubscription(envelope.Subscription);
    }

    public async Task<BillingSubscription> CreateSubscriptionAsync(
        BillingSubscriptionDraft subscription,
        string uniquenessToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        var body = new CreateSubscriptionRequestBody
        {
            UniquenessToken = uniquenessToken,
            Subscription = new CreateSubscriptionPayload
            {
                ProductHandle = subscription.ProductHandle,
                CustomerId = subscription.CustomerId,
                Reference = subscription.Reference
            }
        };

        var envelope = await SendAsync<SubscriptionEnvelope>(
            HttpMethod.Post, "subscriptions.json", body, cancellationToken);
        if (envelope?.Subscription is null)
        {
            throw new MaxioBillingException(
                HttpStatusCode.BadGateway,
                "Maxio did not return a subscription payload.");
        }

        return MapSubscription(envelope.Subscription);
    }

    private void ConfigureHttpClient()
    {
        if (_httpClient.BaseAddress is null)
        {
            var baseUrl = _options.TryResolveBaseUrl();
            if (baseUrl is not null)
            {
                _httpClient.BaseAddress = new Uri(baseUrl, UriKind.Absolute);
            }
        }

        if (_httpClient.Timeout == Timeout.InfiniteTimeSpan || _httpClient.Timeout == TimeSpan.Zero)
        {
            _httpClient.Timeout = TimeSpan.FromSeconds(100);
        }

        if (!_httpClient.DefaultRequestHeaders.Accept.Any())
        {
            _httpClient.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
        }

        if (_httpClient.DefaultRequestHeaders.Authorization is null
            && !string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ApiKey}:X"));
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", token);
        }
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey) || _httpClient.BaseAddress is null)
        {
            throw new MaxioBillingException(
                HttpStatusCode.ServiceUnavailable,
                "Maxio is not configured. Bind Maxio:ApiKey and either Maxio:BaseUrl or Maxio:Subdomain.");
        }

        if (string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw new MaxioBillingException(
                HttpStatusCode.ServiceUnavailable,
                "Maxio is not configured. Bind Maxio:ProductFamilyHandle.");
        }
    }

    private async Task<T?> SendAsync<T>(
        HttpMethod method,
        string relativePath,
        object? body,
        CancellationToken cancellationToken,
        bool treatNotFoundAsEmpty = false)
    {
        EnsureConfigured();

        HttpResponseMessage? response = null;
        for (var attempt = 0; attempt <= MaxRetriesOnTooManyRequests; attempt++)
        {
            using var request = new HttpRequestMessage(method, relativePath);
            if (body is not null)
            {
                var json = JsonSerializer.Serialize(body, MaxioJson.Options);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.StatusCode != HttpStatusCode.TooManyRequests || attempt == MaxRetriesOnTooManyRequests)
            {
                break;
            }

            var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
            _logger.LogWarning(
                "Maxio returned 429 for {Method} {Path}; retrying in {DelaySeconds}s (attempt {Attempt}).",
                method,
                SanitizePath(relativePath),
                delay.TotalSeconds,
                attempt + 1);
            await Task.Delay(delay, cancellationToken);
            response.Dispose();
        }

        using (response)
        {
            var payload = response is null
                ? string.Empty
                : await response.Content.ReadAsStringAsync(cancellationToken);

            if (treatNotFoundAsEmpty && response!.StatusCode == HttpStatusCode.NotFound)
            {
                return default;
            }

            if (response!.StatusCode == HttpStatusCode.Conflict)
            {
                throw new MaxioDuplicateSubmissionException(payload);
            }

            if (!response.IsSuccessStatusCode)
            {
                var status = MapOutgoingStatus(response.StatusCode);
                throw new MaxioBillingException(status, FormatMaxioError(response.StatusCode, payload));
            }

            if (string.IsNullOrWhiteSpace(payload))
            {
                return default;
            }

            try
            {
                return JsonSerializer.Deserialize<T>(payload, MaxioJson.Options);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to deserialize Maxio response for {Path}.", SanitizePath(relativePath));
                throw new MaxioBillingException(
                    HttpStatusCode.BadGateway,
                    "Maxio returned a response that could not be parsed.");
            }
        }
    }

    private static HttpStatusCode MapOutgoingStatus(HttpStatusCode upstream)
    {
        return upstream switch
        {
            HttpStatusCode.BadRequest => HttpStatusCode.BadRequest,
            HttpStatusCode.Unauthorized => HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.Forbidden => HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.NotFound => HttpStatusCode.NotFound,
            HttpStatusCode.Conflict => HttpStatusCode.Conflict,
            (HttpStatusCode)422 => (HttpStatusCode)422,
            HttpStatusCode.TooManyRequests => HttpStatusCode.ServiceUnavailable,
            _ => HttpStatusCode.BadGateway
        };
    }

    private static string FormatMaxioError(HttpStatusCode statusCode, string payload)
    {
        var detail = ExtractErrorDetail(payload);
        if (string.IsNullOrWhiteSpace(detail))
        {
            return $"Maxio request failed with {(int)statusCode}.";
        }

        return $"Maxio request failed with {(int)statusCode}: {detail}";
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
                return Truncate(payload);
            }

            if (errors.ValueKind == JsonValueKind.Array)
            {
                var messages = errors.EnumerateArray()
                    .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString())
                    .Where(message => !string.IsNullOrWhiteSpace(message));
                return string.Join("; ", messages);
            }

            if (errors.ValueKind == JsonValueKind.Object)
            {
                var messages = errors.EnumerateObject()
                    .Select(property => $"{property.Name}: {property.Value}");
                return string.Join("; ", messages);
            }

            if (errors.ValueKind == JsonValueKind.String)
            {
                return errors.GetString() ?? string.Empty;
            }
        }
        catch (JsonException)
        {
            return Truncate(payload);
        }

        return Truncate(payload);
    }

    private static string Truncate(string value) =>
        value.Length <= 500 ? value : value[..500];

    private static string SanitizePath(string relativePath)
    {
        var queryIndex = relativePath.IndexOf('?', StringComparison.Ordinal);
        return queryIndex < 0 ? relativePath : relativePath[..queryIndex];
    }

    private static BillingProduct MapProduct(ProductPayload product) => new()
    {
        Id = product.Id,
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? string.Empty,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? string.Empty
    };

    private static BillingCustomer MapCustomer(CustomerPayload customer) => new()
    {
        Id = customer.Id,
        Reference = customer.Reference,
        Email = customer.Email ?? string.Empty
    };

    private static BillingSubscription MapSubscription(SubscriptionPayload subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State ?? string.Empty,
        ProductPriceInCents = subscription.ProductPriceInCents,
        NextAssessmentAt = subscription.NextAssessmentAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        CreatedAt = subscription.CreatedAt,
        Reference = subscription.Reference,
        ProductHandle = subscription.Product?.Handle,
        ProductName = subscription.Product?.Name
    };
}

/// <summary>
/// Maxio rejected a POST/PUT because the uniqueness_token was already seen (HTTP 409).
/// </summary>
public sealed class MaxioDuplicateSubmissionException : MaxioBillingException
{
    public MaxioDuplicateSubmissionException(string payload)
        : base(HttpStatusCode.Conflict, string.IsNullOrWhiteSpace(payload)
            ? "Maxio rejected a duplicate submission."
            : payload)
    {
    }
}
