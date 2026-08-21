using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Maxio Advanced Billing HTTP client. Authenticates with HTTP Basic (API key as username, "X" as password)
/// against <c>https://{subdomain}.chargify.com</c>, or <see cref="MaxioOptions.BaseUrl"/> when set.
/// </summary>
public class MaxioAdvancedBillingGateway : IMaxioAdvancedBillingGateway
{
    private const int MaxRetries = 3;
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioAdvancedBillingGateway> _logger;

    public MaxioAdvancedBillingGateway(
        HttpClient httpClient,
        IOptions<MaxioOptions> options,
        ILogger<MaxioAdvancedBillingGateway> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        ConfigureClient(_httpClient, _options);
    }

    internal static void ConfigureClient(HttpClient httpClient, MaxioOptions options)
    {
        httpClient.BaseAddress ??= ResolveBaseAddress(options);
        httpClient.DefaultRequestHeaders.Accept.Clear();
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (httpClient.DefaultRequestHeaders.Authorization is null && !string.IsNullOrWhiteSpace(options.ApiKey))
        {
            var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ApiKey}:X"));
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        }
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException(
                "Maxio:ApiKey is not configured. Set MAXIO_API_KEY or the Maxio:ApiKey user secret.");
        }

        if (string.IsNullOrWhiteSpace(_options.Subdomain) && string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            throw new InvalidOperationException(
                "Maxio:Subdomain or Maxio:BaseUrl must be configured.");
        }
    }

    internal static Uri ResolveBaseAddress(MaxioOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            var trimmed = options.BaseUrl.Trim();
            if (!trimmed.EndsWith('/'))
            {
                trimmed += "/";
            }

            return new Uri(trimmed, UriKind.Absolute);
        }

        if (string.IsNullOrWhiteSpace(options.Subdomain))
        {
            throw new InvalidOperationException(
                "Maxio:Subdomain is required when Maxio:BaseUrl is not set.");
        }

        return new Uri($"https://{options.Subdomain}.chargify.com/", UriKind.Absolute);
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListConfiguredFamilyProductsAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw new InvalidOperationException("Maxio:ProductFamilyHandle is not configured.");
        }

        var familyKey = ToHandlePath(_options.ProductFamilyHandle);
        var plans = new List<SubscriptionPlan>();
        var page = 1;
        const int perPage = 200;

        while (true)
        {
            var path = $"product_families/{familyKey}/products.json?page={page}&per_page={perPage}";
            var envelopes = await SendAsync<List<MaxioProductEnvelope>>(HttpMethod.Get, path, null, cancellationToken)
                            ?? new List<MaxioProductEnvelope>();

            foreach (var envelope in envelopes)
            {
                var product = envelope.Product;
                if (product is null || product.ArchivedAt is not null || string.IsNullOrWhiteSpace(product.Handle))
                {
                    continue;
                }

                plans.Add(MapPlan(product));
            }

            if (envelopes.Count < perPage)
            {
                break;
            }

            page++;
        }

        return plans;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        try
        {
            var envelope = await SendAsync<MaxioCustomerEnvelope>(HttpMethod.Get, path, null, cancellationToken);
            return envelope?.Customer is null ? null : MapCustomer(envelope.Customer);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == (int)HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(
        string reference,
        string firstName,
        string lastName,
        string email,
        CancellationToken cancellationToken)
    {
        var body = new CreateMaxioCustomerRequest
        {
            Customer = new CreateMaxioCustomerPayload
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference
            }
        };

        var envelope = await SendAsync<MaxioCustomerEnvelope>(HttpMethod.Post, "customers.json", body, cancellationToken);
        if (envelope?.Customer is null)
        {
            throw new MaxioApiException("Maxio create-customer returned an empty payload.", (int)HttpStatusCode.BadGateway);
        }

        return MapCustomer(envelope.Customer);
    }

    public async Task<CustomerSubscription> CreateSubscriptionAsync(
        int customerId,
        string productHandle,
        string? subscriptionReference,
        string uniquenessToken,
        CancellationToken cancellationToken)
    {
        var body = new CreateMaxioSubscriptionRequest
        {
            UniquenessToken = uniquenessToken,
            Subscription = new CreateMaxioSubscriptionPayload
            {
                ProductHandle = productHandle,
                CustomerId = customerId,
                Reference = subscriptionReference
            }
        };

        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Post, "subscriptions.json", body, cancellationToken);
        if (envelope?.Subscription is null)
        {
            throw new MaxioApiException("Maxio create-subscription returned an empty payload.", (int)HttpStatusCode.BadGateway);
        }

        return MapSubscription(envelope.Subscription);
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken)
    {
        var path = $"customers/{customerId}/subscriptions.json";
        var envelopes = await SendAsync<List<MaxioSubscriptionEnvelope>>(HttpMethod.Get, path, null, cancellationToken)
                        ?? new List<MaxioSubscriptionEnvelope>();

        return envelopes
            .Where(e => e.Subscription is not null)
            .Select(e => MapSubscription(e.Subscription!))
            .ToList();
    }

    public async Task<CustomerSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        try
        {
            var envelope = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Get, path, null, cancellationToken);
            return envelope?.Subscription is null ? null : MapSubscription(envelope.Subscription);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == (int)HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<T?> SendAsync<T>(HttpMethod method, string relativePath, object? body, CancellationToken cancellationToken)
    {
        EnsureConfigured();

        HttpResponseMessage? response = null;
        string? responseText = null;

        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            using var request = new HttpRequestMessage(method, relativePath);
            if (body is not null)
            {
                var json = JsonSerializer.Serialize(body, MaxioJson.SerializerOptions);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            _logger.LogInformation("Maxio {Method} {Path} (attempt {Attempt})", method.Method, relativePath, attempt);
            response = await _httpClient.SendAsync(request, cancellationToken);
            responseText = await response.Content.ReadAsStringAsync(cancellationToken);

            if ((int)response.StatusCode == 429 && attempt < MaxRetries)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                _logger.LogWarning("Maxio returned 429 for {Path}; waiting {Delay} before retry.", relativePath, delay);
                await Task.Delay(delay, cancellationToken);
                continue;
            }

            break;
        }

        if (response is null)
        {
            throw new MaxioApiException("Maxio request failed before a response was received.", (int)HttpStatusCode.BadGateway);
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new MaxioApiException($"Maxio resource not found: {relativePath}", (int)HttpStatusCode.NotFound, responseText);
        }

        if (!response.IsSuccessStatusCode)
        {
            var detail = ExtractErrorMessage(responseText) ?? response.ReasonPhrase ?? "Unknown Maxio error";
            _logger.LogWarning("Maxio {Method} {Path} failed with {Status}: {Detail}",
                method.Method, relativePath, (int)response.StatusCode, detail);
            throw new MaxioApiException($"Maxio request failed ({(int)response.StatusCode}): {detail}",
                (int)response.StatusCode, responseText);
        }

        if (string.IsNullOrWhiteSpace(responseText) || response.StatusCode == HttpStatusCode.NoContent)
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(responseText, MaxioJson.SerializerOptions);
        }
        catch (JsonException ex)
        {
            throw new MaxioApiException($"Failed to parse Maxio response for {relativePath}: {ex.Message}",
                (int)HttpStatusCode.BadGateway, responseText);
        }
    }

    internal static string ToHandlePath(string handle)
    {
        var trimmed = handle.Trim();
        if (trimmed.StartsWith("handle:", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        return $"handle:{trimmed}";
    }

    internal static SubscriptionPlan MapPlan(MaxioProductDto product) => new()
    {
        Id = product.Id,
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? string.Empty,
        Description = product.Description,
        Price = ToMoney(product.PriceInCents),
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? string.Empty
    };

    internal static MaxioCustomer MapCustomer(MaxioCustomerDto customer) => new()
    {
        Id = customer.Id,
        Reference = customer.Reference,
        Email = customer.Email ?? string.Empty
    };

    internal static CustomerSubscription MapSubscription(MaxioSubscriptionDto subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State ?? string.Empty,
        PlanHandle = subscription.Product?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? string.Empty,
        Price = ToMoney(subscription.ProductPriceInCents != 0
            ? subscription.ProductPriceInCents
            : subscription.Product?.PriceInCents ?? 0),
        NextBillingAt = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
        Reference = subscription.Reference
    };

    internal static decimal ToMoney(long cents) => cents / 100m;

    internal static string? ExtractErrorMessage(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (!doc.RootElement.TryGetProperty("errors", out var errors))
            {
                return responseBody;
            }

            if (errors.ValueKind == JsonValueKind.Array)
            {
                var parts = errors.EnumerateArray()
                    .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : e.ToString())
                    .Where(s => !string.IsNullOrWhiteSpace(s));
                return string.Join("; ", parts);
            }

            if (errors.ValueKind == JsonValueKind.Object)
            {
                var parts = errors.EnumerateObject()
                    .Select(p => $"{p.Name}: {p.Value}");
                return string.Join("; ", parts);
            }

            if (errors.ValueKind == JsonValueKind.String)
            {
                return errors.GetString();
            }
        }
        catch (JsonException)
        {
            return responseBody;
        }

        return responseBody;
    }
}
