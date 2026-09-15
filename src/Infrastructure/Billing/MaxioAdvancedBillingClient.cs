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
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public class MaxioAdvancedBillingClient : IMaxioAdvancedBillingClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private const int MaxAttempts = 4;
    private const int ListPageSize = 200;

    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioAdvancedBillingClient> _logger;
    private readonly AuthenticationHeaderValue _authorization;

    public MaxioAdvancedBillingClient(
        HttpClient httpClient,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioAdvancedBillingClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
        _authorization = BuildAuthorization(_settings.ApiKey);
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var handle = Uri.EscapeDataString(_settings.ProductFamilyHandle);
        var plans = new List<SubscriptionPlan>();

        for (var page = 1; ; page++)
        {
            var path =
                $"product_families/handle:{handle}/products.json?page={page}&per_page={ListPageSize}&include_archived=false";
            var envelopes = await SendAsync<List<MaxioProductEnvelope>>(HttpMethod.Get, path, null, cancellationToken)
                            ?? new List<MaxioProductEnvelope>();

            foreach (var envelope in envelopes)
            {
                var product = envelope.Product;
                if (product is null || product.ArchivedAt is not null || string.IsNullOrWhiteSpace(product.Handle))
                {
                    continue;
                }

                plans.Add(new SubscriptionPlan
                {
                    Handle = product.Handle,
                    Name = product.Name,
                    Description = product.Description ?? string.Empty,
                    Price = CentsToDecimal(product.PriceInCents),
                    Interval = product.Interval,
                    IntervalUnit = product.IntervalUnit,
                    RequiresPaymentMethod = product.RequireCreditCard
                });
            }

            if (envelopes.Count < ListPageSize)
            {
                break;
            }
        }

        return plans;
    }

    public async Task<BillingCustomer?> FindCustomerByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var envelope = await SendAsync<MaxioCustomerEnvelope>(
            HttpMethod.Get, path, null, cancellationToken, treatNotFoundAsNull: true);
        return MapCustomer(envelope?.Customer);
    }

    public async Task<BillingCustomer> CreateCustomerAsync(
        string reference,
        string firstName,
        string lastName,
        string email,
        string uniquenessToken,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var body = new MaxioCreateCustomerRequest
        {
            UniquenessToken = uniquenessToken,
            Customer = new MaxioCreateCustomerPayload
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference
            }
        };

        var envelope = await SendAsync<MaxioCustomerEnvelope>(
            HttpMethod.Post, "customers.json", body, cancellationToken);
        var customer = MapCustomer(envelope?.Customer);
        if (customer is null)
        {
            throw new MaxioApiException(HttpStatusCode.BadGateway, "Maxio created a customer but returned no payload.");
        }

        return customer;
    }

    public async Task<IReadOnlyList<ShopperSubscription>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var subscriptions = new List<ShopperSubscription>();
        for (var page = 1; ; page++)
        {
            var path = $"customers/{customerId}/subscriptions.json?page={page}&per_page={ListPageSize}";
            var envelopes = await SendAsync<List<MaxioSubscriptionEnvelope>>(
                HttpMethod.Get, path, null, cancellationToken) ?? new List<MaxioSubscriptionEnvelope>();

            subscriptions.AddRange(envelopes
                .Select(e => MapSubscription(e.Subscription))
                .Where(s => s is not null)!);

            if (envelopes.Count < ListPageSize)
            {
                break;
            }
        }

        return subscriptions;
    }

    public async Task<ShopperSubscription> CreateSubscriptionAsync(
        int customerId,
        string productHandle,
        string uniquenessToken,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var body = new MaxioCreateSubscriptionRequest
        {
            UniquenessToken = uniquenessToken,
            Subscription = new MaxioCreateSubscriptionPayload
            {
                CustomerId = customerId,
                ProductHandle = productHandle
            }
        };

        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(
            HttpMethod.Post, "subscriptions.json", body, cancellationToken);
        var subscription = MapSubscription(envelope?.Subscription);
        if (subscription is null)
        {
            throw new MaxioApiException(HttpStatusCode.BadGateway, "Maxio created a subscription but returned no payload.");
        }

        return subscription;
    }

    private async Task<T?> SendAsync<T>(
        HttpMethod method,
        string relativePath,
        object? body,
        CancellationToken cancellationToken,
        bool treatNotFoundAsNull = false)
        where T : class
    {
        Exception? lastTransient = null;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            using var request = new HttpRequestMessage(method, BuildUri(relativePath));
            request.Headers.Authorization = _authorization;
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            if (body is not null)
            {
                var json = JsonSerializer.Serialize(body, JsonOptions);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(request, cancellationToken);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                lastTransient = new MaxioApiException(HttpStatusCode.GatewayTimeout, "The Maxio Billing API request timed out.");
                await DelayBeforeRetryAsync(attempt, cancellationToken);
                continue;
            }
            catch (HttpRequestException ex)
            {
                lastTransient = new MaxioApiException(HttpStatusCode.BadGateway, $"Unable to reach Maxio Billing API: {ex.Message}");
                await DelayBeforeRetryAsync(attempt, cancellationToken);
                continue;
            }

            using (response)
            {
                if (treatNotFoundAsNull && response.StatusCode == HttpStatusCode.NotFound)
                {
                    return null;
                }

                if (response.StatusCode == (HttpStatusCode)429 || (int)response.StatusCode >= 500)
                {
                    var throttleBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogWarning(
                        "Maxio Billing API returned {StatusCode} for {Method} {Path} (attempt {Attempt}/{MaxAttempts}).",
                        (int)response.StatusCode, method, relativePath, attempt, MaxAttempts);
                    lastTransient = new MaxioApiException(
                        response.StatusCode,
                        FormatError(response.StatusCode, throttleBody));
                    await DelayBeforeRetryAsync(attempt, cancellationToken);
                    continue;
                }

                var payload = await response.Content.ReadAsStringAsync(cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    throw new MaxioApiException(response.StatusCode, FormatError(response.StatusCode, payload));
                }

                if (string.IsNullOrWhiteSpace(payload))
                {
                    return null;
                }

                return JsonSerializer.Deserialize<T>(payload, JsonOptions);
            }
        }

        throw lastTransient ?? new MaxioApiException(HttpStatusCode.BadGateway, "The Maxio Billing API request failed.");
    }

    private Uri BuildUri(string relativePath)
    {
        var baseUrl = _settings.ResolveBaseUrl().TrimEnd('/');
        return new Uri($"{baseUrl}/{relativePath.TrimStart('/')}");
    }

    private void EnsureConfigured()
    {
        if (_settings.IsConfigured())
        {
            return;
        }

        throw new MaxioConfigurationException(
            "Maxio Billing is not configured. Set Maxio:ApiKey, Maxio:Subdomain (or Maxio:BaseUrl), and Maxio:ProductFamilyHandle.");
    }

    private static AuthenticationHeaderValue BuildAuthorization(string apiKey)
    {
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{apiKey}:X"));
        return new AuthenticationHeaderValue("Basic", token);
    }

    private static BillingCustomer? MapCustomer(MaxioCustomerDto? dto)
    {
        if (dto is null || dto.Id == 0)
        {
            return null;
        }

        return new BillingCustomer
        {
            Id = dto.Id,
            Reference = dto.Reference ?? string.Empty,
            Email = dto.Email
        };
    }

    private static ShopperSubscription? MapSubscription(MaxioSubscriptionDto? dto)
    {
        if (dto is null || dto.Id == 0)
        {
            return null;
        }

        return new ShopperSubscription
        {
            Id = dto.Id,
            CustomerId = dto.Customer?.Id ?? 0,
            PlanHandle = dto.Product?.Handle ?? string.Empty,
            PlanName = dto.Product?.Name ?? string.Empty,
            Price = CentsToDecimal(dto.ProductPriceInCents != 0 ? dto.ProductPriceInCents : dto.Product?.PriceInCents ?? 0),
            State = dto.State,
            NextBillingAt = dto.NextAssessmentAt ?? dto.CurrentPeriodEndsAt,
            CreatedAt = dto.CreatedAt
        };
    }

    private static decimal CentsToDecimal(long cents) => cents / 100m;

    private static string FormatError(HttpStatusCode statusCode, string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return $"Maxio Billing API returned {(int)statusCode} {statusCode}.";
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.TryGetProperty("errors", out var errors))
            {
                var messages = FlattenErrors(errors);
                if (messages.Count > 0)
                {
                    return string.Join(" ", messages);
                }
            }
        }
        catch (JsonException)
        {
            // Fall through to raw payload.
        }

        return $"Maxio Billing API returned {(int)statusCode} {statusCode}: {payload}";
    }

    private static List<string> FlattenErrors(JsonElement errors)
    {
        var messages = new List<string>();
        switch (errors.ValueKind)
        {
            case JsonValueKind.String:
                messages.Add(errors.GetString() ?? string.Empty);
                break;
            case JsonValueKind.Array:
                foreach (var item in errors.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        messages.Add(item.GetString() ?? string.Empty);
                    }
                    else
                    {
                        messages.Add(item.ToString());
                    }
                }
                break;
            case JsonValueKind.Object:
                foreach (var property in errors.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in property.Value.EnumerateArray())
                        {
                            messages.Add($"{property.Name}: {item}");
                        }
                    }
                    else
                    {
                        messages.Add($"{property.Name}: {property.Value}");
                    }
                }
                break;
        }

        return messages.Where(m => !string.IsNullOrWhiteSpace(m)).ToList();
    }

    private static Task DelayBeforeRetryAsync(int attempt, CancellationToken cancellationToken)
    {
        if (attempt >= MaxAttempts)
        {
            return Task.CompletedTask;
        }

        var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
        return Task.Delay(delay, cancellationToken);
    }
}
