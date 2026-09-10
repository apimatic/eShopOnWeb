using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// HTTP transport over the Maxio Advanced Billing (Chargify) REST API. The injected
/// <see cref="HttpClient"/> is pre-configured (base address, HTTP Basic auth) at registration.
/// </summary>
public class MaxioClient : IMaxioClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private const int MaxAttempts = 3;

    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioClient> _logger;

    public MaxioClient(HttpClient httpClient, MaxioSettings settings, ILogger<MaxioClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        if (string.IsNullOrWhiteSpace(productFamilyHandle))
        {
            throw new SubscriptionConfigurationException(
                "No Maxio product family handle is configured (Maxio:ProductFamilyHandle).");
        }

        var path = $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json";
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, path), cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new SubscriptionConfigurationException(
                $"Maxio product family '{productFamilyHandle}' was not found on this site.");
        }

        await EnsureSuccessAsync(response, cancellationToken);

        var envelopes = await response.Content.ReadFromJsonAsync<List<MaxioProductEnvelope>>(JsonOptions, cancellationToken)
                        ?? new List<MaxioProductEnvelope>();

        var plans = new List<SubscriptionPlan>();
        foreach (var envelope in envelopes)
        {
            var product = envelope.Product;
            if (product is null || product.ArchivedAt is not null)
            {
                continue;
            }

            plans.Add(new SubscriptionPlan
            {
                Id = product.Id,
                Handle = product.Handle ?? string.Empty,
                Name = product.Name ?? string.Empty,
                Description = product.Description,
                PriceInCents = product.PriceInCents,
                Interval = product.Interval,
                IntervalUnit = product.IntervalUnit ?? string.Empty
            });
        }

        return plans;
    }

    public async Task<MaxioCustomerReference?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, path), cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);

        var envelope = await response.Content.ReadFromJsonAsync<MaxioCustomerEnvelope>(JsonOptions, cancellationToken);
        return ToCustomerReference(envelope?.Customer);
    }

    public async Task<MaxioCustomerReference> CreateCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var body = new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCustomerAttributes
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference
            }
        };

        using var response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Post, "customers.json")
            {
                Content = JsonContent.Create(body, options: JsonOptions)
            },
            cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);

        var envelope = await response.Content.ReadFromJsonAsync<MaxioCustomerEnvelope>(JsonOptions, cancellationToken);
        var customer = ToCustomerReference(envelope?.Customer)
                       ?? throw new MaxioApiException(response.StatusCode, "Customer creation returned no customer.");
        return customer;
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var path = $"customers/{customerId}/subscriptions.json";
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, path), cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return Array.Empty<CustomerSubscription>();
        }

        await EnsureSuccessAsync(response, cancellationToken);

        var envelopes = await response.Content.ReadFromJsonAsync<List<MaxioSubscriptionEnvelope>>(JsonOptions, cancellationToken)
                        ?? new List<MaxioSubscriptionEnvelope>();

        var subscriptions = new List<CustomerSubscription>();
        foreach (var envelope in envelopes)
        {
            if (envelope.Subscription is not null)
            {
                subscriptions.Add(ToCustomerSubscription(envelope.Subscription));
            }
        }

        return subscriptions;
    }

    public async Task<CustomerSubscription> CreateSubscriptionAsync(long customerId, string planHandle, string uniquenessToken, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var body = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioSubscriptionAttributes
            {
                ProductHandle = planHandle,
                CustomerId = customerId,
                PaymentCollectionMethod = _settings.PaymentCollectionMethod
            },
            UniquenessToken = uniquenessToken
        };

        using var response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Post, "subscriptions.json")
            {
                Content = JsonContent.Create(body, options: JsonOptions)
            },
            cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);

        var envelope = await response.Content.ReadFromJsonAsync<MaxioSubscriptionEnvelope>(JsonOptions, cancellationToken);
        var subscription = envelope?.Subscription
                           ?? throw new MaxioApiException(response.StatusCode, "Subscription creation returned no subscription.");
        return ToCustomerSubscription(subscription);
    }

    private void EnsureConfigured()
    {
        if (!_settings.IsConfigured)
        {
            throw new SubscriptionConfigurationException(
                "Maxio subscription billing is not configured. Set Maxio:ApiKey and Maxio:Subdomain "
                + "(or Maxio:BaseUrl) via user-secrets or environment configuration.");
        }
    }

    /// <summary>
    /// Sends a request, retrying transient failures (HTTP 429 and 5xx, and network errors)
    /// with exponential backoff. Maxio throttles on concurrency, so retries are serial.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> requestFactory, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            HttpResponseMessage? response = null;
            try
            {
                using var request = requestFactory();
                response = await _httpClient.SendAsync(request, cancellationToken);

                if (attempt < MaxAttempts && IsTransient(response.StatusCode))
                {
                    _logger.LogWarning("Maxio request to {Path} returned {StatusCode}; retry {Attempt}/{Max}.",
                        request.RequestUri, (int)response.StatusCode, attempt, MaxAttempts);
                    response.Dispose();
                    await DelayForAttemptAsync(attempt, cancellationToken);
                    continue;
                }

                return response;
            }
            catch (HttpRequestException ex) when (attempt < MaxAttempts)
            {
                response?.Dispose();
                _logger.LogWarning(ex, "Maxio request failed (network error); retry {Attempt}/{Max}.", attempt, MaxAttempts);
                await DelayForAttemptAsync(attempt, cancellationToken);
            }
        }
    }

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.TooManyRequests || (int)statusCode >= 500;

    private static Task DelayForAttemptAsync(int attempt, CancellationToken cancellationToken) =>
        Task.Delay(TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt - 1)), cancellationToken);

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var message = await ReadErrorMessageAsync(response, cancellationToken);
        _logger.LogError("Maxio API error {StatusCode}: {Message}", (int)response.StatusCode, message);
        throw new MaxioApiException(response.StatusCode, message);
    }

    private static async Task<string> ReadErrorMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return response.ReasonPhrase ?? "Unknown error";
            }

            using var document = JsonDocument.Parse(raw);
            if (document.RootElement.TryGetProperty("errors", out var errors))
            {
                return errors.ValueKind switch
                {
                    JsonValueKind.Array => string.Join("; ", EnumerateStrings(errors)),
                    JsonValueKind.String => errors.GetString() ?? raw,
                    _ => errors.ToString()
                };
            }

            return raw;
        }
        catch (JsonException)
        {
            return response.ReasonPhrase ?? "Unknown error";
        }
    }

    private static IEnumerable<string> EnumerateStrings(JsonElement array)
    {
        foreach (var element in array.EnumerateArray())
        {
            yield return element.ValueKind == JsonValueKind.String ? element.GetString() ?? string.Empty : element.ToString();
        }
    }

    private static MaxioCustomerReference? ToCustomerReference(MaxioCustomer? customer) =>
        customer is null ? null : new MaxioCustomerReference(customer.Id, customer.Reference, customer.Email);

    private static CustomerSubscription ToCustomerSubscription(MaxioSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State ?? string.Empty,
        PlanHandle = subscription.Product?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? string.Empty,
        PriceInCents = subscription.Product?.PriceInCents ?? 0,
        Interval = subscription.Product?.Interval ?? 0,
        IntervalUnit = subscription.Product?.IntervalUnit ?? string.Empty,
        NextBillingAt = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CreatedAt = subscription.CreatedAt,
        CustomerId = subscription.Customer?.Id ?? 0,
        CustomerReference = subscription.Customer?.Reference
    };
}
