using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Typed Maxio Advanced Billing client. Paths, auth, and payloads follow
/// <c>maxio-spec/openapi.yaml</c>.
/// </summary>
public class MaxioBillingGateway : ISubscriptionBillingGateway
{
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioBillingGateway> _logger;

    public MaxioBillingGateway(
        HttpClient httpClient,
        IOptions<MaxioOptions> options,
        ILogger<MaxioBillingGateway> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<BillingProduct>> ListProductsForFamilyAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var family = Uri.EscapeDataString($"handle:{_options.ProductFamilyHandle}");
        var path = $"product_families/{family}/products.json?per_page=200";
        var wrapped = await SendAsync<List<ProductResponse>>(HttpMethod.Get, path, null, cancellationToken);
        var products = new List<BillingProduct>();
        if (wrapped is null)
        {
            return products;
        }

        foreach (var item in wrapped)
        {
            if (item.Product is null)
            {
                continue;
            }

            products.Add(ToBillingProduct(item.Product));
        }

        return products;
    }

    public async Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var response = await SendAsync<CustomerResponse>(HttpMethod.Get, path, null, cancellationToken, allowNotFound: true);
        return response?.Customer is null ? null : ToBillingCustomer(response.Customer);
    }

    public async Task<BillingCustomer> CreateCustomerAsync(Shopper shopper, string reference, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var (firstName, lastName) = ShopperNameFormatter.Split(shopper);
        var payload = new CreateCustomerRequest
        {
            Customer = new CreateCustomerAttributes
            {
                FirstName = firstName,
                LastName = lastName,
                Email = shopper.Email,
                Reference = reference
            }
        };

        var response = await SendAsync<CustomerResponse>(HttpMethod.Post, "customers.json", payload, cancellationToken);
        if (response?.Customer is null)
        {
            throw new MaxioApiException(HttpStatusCode.BadGateway, "Maxio created a customer without a body.");
        }

        return ToBillingCustomer(response.Customer);
    }

    public async Task<IReadOnlyList<BillingSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var path = $"customers/{customerId}/subscriptions.json";
        var wrapped = await SendAsync<List<SubscriptionResponse>>(HttpMethod.Get, path, null, cancellationToken);
        var subscriptions = new List<BillingSubscription>();
        if (wrapped is null)
        {
            return subscriptions;
        }

        foreach (var item in wrapped)
        {
            if (item.Subscription is null)
            {
                continue;
            }

            subscriptions.Add(ToBillingSubscription(item.Subscription));
        }

        return subscriptions;
    }

    public async Task<BillingSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var response = await SendAsync<SubscriptionResponse>(HttpMethod.Get, path, null, cancellationToken, allowNotFound: true);
        return response?.Subscription is null ? null : ToBillingSubscription(response.Subscription);
    }

    public async Task<BillingSubscription> CreateSubscriptionAsync(
        int customerId,
        string productHandle,
        string reference,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var payload = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscriptionAttributes
            {
                ProductHandle = productHandle,
                CustomerId = customerId,
                Reference = reference
            }
        };

        var response = await SendAsync<SubscriptionResponse>(HttpMethod.Post, "subscriptions.json", payload, cancellationToken);
        if (response?.Subscription is null)
        {
            throw new MaxioApiException(HttpStatusCode.BadGateway, "Maxio created a subscription without a body.");
        }

        return ToBillingSubscription(response.Subscription);
    }

    public static void ConfigureHttpClient(HttpClient client, MaxioOptions options)
    {
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (!options.IsConfigured)
        {
            return;
        }

        client.BaseAddress = new Uri(options.GetApiBaseUrl(), UriKind.Absolute);
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ApiKey}:x"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
    }

    private void EnsureConfigured()
    {
        if (!_options.IsConfigured)
        {
            throw new InvalidOperationException(
                "Maxio billing is not configured. Set Maxio:ApiKey, Maxio:Subdomain (or Maxio:BaseUrl), and Maxio:ProductFamilyHandle.");
        }

        if (_httpClient.BaseAddress is null)
        {
            ConfigureHttpClient(_httpClient, _options);
        }
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
            var json = JsonSerializer.Serialize(body, MaxioJson.SerializerOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        _logger.LogInformation("Maxio {Method} {Path}", method.Method, relativePath);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }

        if (!response.IsSuccessStatusCode)
        {
            var errors = ParseErrors(content);
            var summary = errors.Count > 0
                ? string.Join(" ", errors)
                : $"Maxio request failed with {(int)response.StatusCode}.";
            _logger.LogWarning("Maxio {Method} {Path} failed with {StatusCode}: {Summary}",
                method.Method, relativePath, (int)response.StatusCode, summary);
            throw new MaxioApiException(response.StatusCode, summary, errors);
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(content, MaxioJson.SerializerOptions);
    }

    private static IReadOnlyList<string> ParseErrors(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return Array.Empty<string>();
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<ErrorListResponse>(content, MaxioJson.SerializerOptions);
            if (parsed?.Errors is { Count: > 0 })
            {
                return parsed.Errors;
            }
        }
        catch (JsonException)
        {
            // Fall through and surface the raw body.
        }

        return new[] { content };
    }

    private static BillingProduct ToBillingProduct(ProductDto product) =>
        new()
        {
            Id = product.Id,
            Handle = product.Handle,
            Name = product.Name ?? string.Empty,
            Description = product.Description,
            PriceInCents = product.PriceInCents,
            Interval = product.Interval,
            IntervalUnit = product.IntervalUnit ?? string.Empty,
            ArchivedAt = product.ArchivedAt?.ToString("O")
        };

    private static BillingCustomer ToBillingCustomer(CustomerDto customer) =>
        new()
        {
            Id = customer.Id,
            Reference = customer.Reference,
            Email = customer.Email ?? string.Empty,
            FirstName = customer.FirstName ?? string.Empty,
            LastName = customer.LastName ?? string.Empty
        };

    private static BillingSubscription ToBillingSubscription(SubscriptionDto subscription) =>
        new()
        {
            Id = subscription.Id,
            State = subscription.State ?? string.Empty,
            Reference = subscription.Reference,
            ProductPriceInCents = subscription.ProductPriceInCents,
            NextAssessmentAt = subscription.NextAssessmentAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            CreatedAt = subscription.CreatedAt,
            ProductHandle = subscription.Product?.Handle,
            ProductName = subscription.Product?.Name ?? string.Empty
        };
}
