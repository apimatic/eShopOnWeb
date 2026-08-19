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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public sealed class MaxioBillingGateway : IMaxioBillingGateway
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;

    public MaxioBillingGateway(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var familyHandle = Uri.EscapeDataString(_options.ProductFamilyHandle.Trim());
        var payload = await SendAsync<List<MaxioProductEnvelope>>(
            HttpMethod.Get,
            $"product_families/handle:{familyHandle}/products.json?per_page=200",
            null,
            cancellationToken);

        return (payload ?? new List<MaxioProductEnvelope>())
            .Select(item => item.Product)
            .Where(product => product is not null && string.IsNullOrEmpty(product.ArchivedAt) && !string.IsNullOrWhiteSpace(product.Handle))
            .Select(product => new SubscriptionPlan(
                product!.Handle!,
                product.Name ?? product.Handle!,
                product.Description,
                checked((int)product.PriceInCents),
                product.Interval,
                product.IntervalUnit ?? "month",
                product.RequireCreditCard))
            .ToList();
    }

    public async Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var payload = await SendAsync<MaxioCustomerEnvelope>(
            HttpMethod.Get,
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}",
            null,
            cancellationToken,
            treatNotFoundAsNull: true);

        return payload?.Customer is null ? null : ToCustomer(payload.Customer);
    }

    public async Task<BillingCustomer> CreateCustomerAsync(
        CreateBillingCustomer customer,
        string uniquenessToken,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var body = new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCreateCustomerBody
            {
                FirstName = customer.FirstName,
                LastName = customer.LastName,
                Email = customer.Email,
                Reference = customer.Reference
            },
            UniquenessToken = uniquenessToken
        };

        var payload = await SendAsync<MaxioCustomerEnvelope>(HttpMethod.Post, "customers.json", body, cancellationToken);
        if (payload?.Customer is null)
        {
            throw new BillingException("Maxio created a customer but returned an empty body.");
        }

        return ToCustomer(payload.Customer);
    }

    public async Task<SubscriptionDetails?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var payload = await SendAsync<MaxioSubscriptionEnvelope>(
            HttpMethod.Get,
            $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}",
            null,
            cancellationToken,
            treatNotFoundAsNull: true);

        return payload?.Subscription is null ? null : ToSubscription(payload.Subscription);
    }

    public async Task<IReadOnlyList<SubscriptionDetails>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var payload = await SendAsync<List<MaxioSubscriptionEnvelope>>(
            HttpMethod.Get,
            $"customers/{customerId}/subscriptions.json",
            null,
            cancellationToken);

        return (payload ?? new List<MaxioSubscriptionEnvelope>())
            .Select(item => item.Subscription)
            .Where(subscription => subscription is not null)
            .Select(subscription => ToSubscription(subscription!))
            .ToList();
    }

    public async Task<SubscriptionDetails> CreateSubscriptionAsync(
        CreateBillingSubscription subscription,
        string uniquenessToken,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var body = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscriptionBody
            {
                ProductHandle = subscription.ProductHandle,
                CustomerId = subscription.CustomerId,
                Reference = subscription.Reference,
                PaymentCollectionMethod = subscription.PaymentCollectionMethod
            },
            UniquenessToken = uniquenessToken
        };

        var payload = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Post, "subscriptions.json", body, cancellationToken);
        if (payload?.Subscription is null)
        {
            throw new BillingException("Maxio created a subscription but returned an empty body.");
        }

        return ToSubscription(payload.Subscription);
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new BillingConfigurationException("Maxio:ApiKey is not configured.");
        }

        if (string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw new BillingConfigurationException("Maxio:ProductFamilyHandle is not configured.");
        }

        if (string.IsNullOrWhiteSpace(_options.BaseUrl) && string.IsNullOrWhiteSpace(_options.Subdomain))
        {
            throw new BillingConfigurationException("Maxio:Subdomain or Maxio:BaseUrl must be configured.");
        }
    }

    private async Task<T?> SendAsync<T>(
        HttpMethod method,
        string relativeUrl,
        object? body,
        CancellationToken cancellationToken,
        bool treatNotFoundAsNull = false)
    {
        using var request = new HttpRequestMessage(method, relativeUrl);
        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, SerializerOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (treatNotFoundAsNull && response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }

        if (response.IsSuccessStatusCode)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return default;
            }

            return JsonSerializer.Deserialize<T>(content, SerializerOptions);
        }

        throw MapError(response.StatusCode, content);
    }

    internal static Exception MapError(HttpStatusCode statusCode, string content)
    {
        var message = ParseErrorMessage(content) ?? $"Maxio request failed with {(int)statusCode}.";

        return statusCode switch
        {
            HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity => new BillingValidationException(message),
            HttpStatusCode.NotFound => new BillingNotFoundException(message),
            HttpStatusCode.Conflict => new BillingConflictException(message),
            HttpStatusCode.TooManyRequests => new BillingRateLimitedException(message),
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new BillingException(
                "Maxio rejected the API credentials.", statusCode),
            _ => new BillingException(message, statusCode)
        };
    }

    internal static string? ParseErrorMessage(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(content);
            if (!document.RootElement.TryGetProperty("errors", out var errors))
            {
                return content.Length > 500 ? content[..500] : content;
            }

            if (errors.ValueKind == JsonValueKind.Array)
            {
                var parts = errors.EnumerateArray()
                    .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString())
                    .Where(item => !string.IsNullOrWhiteSpace(item));
                return string.Join("; ", parts);
            }

            if (errors.ValueKind == JsonValueKind.Object)
            {
                var parts = errors.EnumerateObject()
                    .Select(property => $"{property.Name}: {property.Value}");
                return string.Join("; ", parts);
            }

            return errors.ToString();
        }
        catch (JsonException)
        {
            return content.Length > 500 ? content[..500] : content;
        }
    }

    private static BillingCustomer ToCustomer(MaxioCustomerDto dto) =>
        new(dto.Id, dto.Reference, dto.Email ?? string.Empty);

    private static SubscriptionDetails ToSubscription(MaxioSubscriptionDto dto) =>
        new(
            dto.Id,
            dto.Reference,
            dto.State ?? string.Empty,
            checked((int)dto.ProductPriceInCents),
            dto.Product?.Handle,
            dto.Product?.Name,
            ParseTimestamp(dto.CurrentPeriodEndsAt),
            ParseTimestamp(dto.NextAssessmentAt));

    private static DateTimeOffset? ParseTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    }
}
