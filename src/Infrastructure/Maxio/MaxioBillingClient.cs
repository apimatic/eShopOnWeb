using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public sealed class MaxioBillingClient : IMaxioBillingClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

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
    }

    public bool IsConfigured => _options.IsConfigured;

    public async Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var handle = Uri.EscapeDataString(_options.ProductFamilyHandle);
        var path = $"product_families/handle:{handle}/products.json?include_archived=false&per_page=200";
        var envelopes = await SendAsync<List<ProductEnvelope>>(HttpMethod.Get, path, null, cancellationToken)
                        ?? new List<ProductEnvelope>();

        return envelopes
            .Select(e => e.Product)
            .Where(p => p is not null && !string.IsNullOrWhiteSpace(p!.Handle))
            .Select(p => new BillingPlan
            {
                Handle = p!.Handle!,
                Name = p.Name ?? p.Handle!,
                Description = p.Description,
                PriceInCents = p.PriceInCents,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit ?? "month",
                RequireCreditCard = p.RequireCreditCard,
                ArchivedAt = ParseTimestamp(p.ArchivedAt)
            })
            .ToList();
    }

    public async Task<BillingCustomer?> FindCustomerByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        try
        {
            var envelope = await SendAsync<CustomerEnvelope>(HttpMethod.Get, path, null, cancellationToken);
            return MapCustomer(envelope?.Customer);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == (int)HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<BillingCustomer> CreateCustomerAsync(
        ShopperIdentity shopper,
        string uniquenessToken,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var body = new CreateCustomerRequest
        {
            UniquenessToken = uniquenessToken,
            Customer = new CreateCustomerPayload
            {
                FirstName = shopper.FirstName,
                LastName = shopper.LastName,
                Email = shopper.Email,
                Organization = "eShopOnWeb",
                Reference = shopper.UserId
            }
        };

        var envelope = await SendAsync<CustomerEnvelope>(HttpMethod.Post, "customers.json", body, cancellationToken);
        var customer = MapCustomer(envelope?.Customer);
        if (customer is null)
        {
            throw new MaxioApiException(502, "Maxio created a customer but returned an empty body.");
        }

        return customer;
    }

    public async Task<IReadOnlyList<BillingSubscription>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var envelopes = await SendAsync<List<SubscriptionEnvelope>>(
                            HttpMethod.Get,
                            $"customers/{customerId}/subscriptions.json",
                            null,
                            cancellationToken)
                        ?? new List<SubscriptionEnvelope>();

        return envelopes
            .Select(e => MapSubscription(e.Subscription))
            .Where(s => s is not null)
            .Select(s => s!)
            .ToList();
    }

    public async Task<BillingSubscription> CreateSubscriptionAsync(
        int customerId,
        string productHandle,
        string uniquenessToken,
        bool paymentMethodRequired,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var body = new CreateSubscriptionRequest
        {
            UniquenessToken = uniquenessToken,
            Subscription = new CreateSubscriptionPayload
            {
                CustomerId = customerId,
                ProductHandle = productHandle,
                PaymentCollectionMethod = paymentMethodRequired
                    ? null
                    : await ResolveInvoiceCollectionMethodAsync(cancellationToken)
            }
        };

        var envelope = await SendAsync<SubscriptionEnvelope>(HttpMethod.Post, "subscriptions.json", body, cancellationToken);
        var subscription = MapSubscription(envelope?.Subscription);
        if (subscription is null)
        {
            throw new MaxioApiException(502, "Maxio created a subscription but returned an empty body.");
        }

        return subscription;
    }

    private static string? _invoiceCollectionMethod;
    private static readonly SemaphoreSlim SiteSettingsGate = new(1, 1);

    private async Task<string> ResolveInvoiceCollectionMethodAsync(CancellationToken cancellationToken)
    {
        if (_invoiceCollectionMethod is not null)
        {
            return _invoiceCollectionMethod;
        }

        await SiteSettingsGate.WaitAsync(cancellationToken);
        try
        {
            if (_invoiceCollectionMethod is not null)
            {
                return _invoiceCollectionMethod;
            }

            var envelope = await SendAsync<SiteEnvelope>(HttpMethod.Get, "site.json", null, cancellationToken);
            // Relationship Invoicing uses remittance; legacy Statements architecture uses invoice.
            _invoiceCollectionMethod = envelope?.Site?.RelationshipInvoicingEnabled == false
                ? "invoice"
                : "remittance";
            return _invoiceCollectionMethod;
        }
        finally
        {
            SiteSettingsGate.Release();
        }
    }

    private async Task<T?> SendAsync<T>(HttpMethod method, string relativePath, object? body, CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;
        HttpResponseMessage? response = null;
        var payload = string.Empty;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var request = new HttpRequestMessage(method, relativePath);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (body is not null)
            {
                request.Content = JsonContent.Create(body, options: JsonOptions);
            }

            response = await _httpClient.SendAsync(request, cancellationToken);
            payload = await response.Content.ReadAsStringAsync(cancellationToken);

            if ((int)response.StatusCode == 429 && attempt < maxAttempts)
            {
                var delay = TimeSpan.FromSeconds(2 * attempt);
                _logger.LogWarning("Maxio rate-limited {Method} {Path}; retrying in {Delay}.", method, relativePath, delay);
                await Task.Delay(delay, cancellationToken);
                continue;
            }

            break;
        }

        if (response is null)
        {
            throw new MaxioApiException(502, "Maxio request failed before a response was received.");
        }

        if (response.IsSuccessStatusCode)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                return default;
            }

            return JsonSerializer.Deserialize<T>(payload, JsonOptions);
        }

        var status = (int)response.StatusCode;
        var message = ExtractErrorMessage(payload) ?? $"Maxio API returned HTTP {status} for {method} {relativePath}.";

        if (status is 401 or 403)
        {
            throw new MaxioApiException(503, "Maxio rejected the configured API credentials.");
        }

        throw new MaxioApiException(status, message);
    }

    private static string? ExtractErrorMessage(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.TryGetProperty("errors", out var errors))
            {
                if (errors.ValueKind == JsonValueKind.Array)
                {
                    var parts = new List<string>();
                    foreach (var item in errors.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            parts.Add(item.GetString() ?? string.Empty);
                        }
                    }

                    if (parts.Count > 0)
                    {
                        return string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
                    }
                }
                else if (errors.ValueKind == JsonValueKind.Object)
                {
                    var parts = new List<string>();
                    foreach (var property in errors.EnumerateObject())
                    {
                        parts.Add($"{property.Name}: {property.Value}");
                    }

                    if (parts.Count > 0)
                    {
                        return string.Join(" ", parts);
                    }
                }
                else if (errors.ValueKind == JsonValueKind.String)
                {
                    return errors.GetString();
                }
            }

            if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
            {
                return error.GetString();
            }
        }
        catch (JsonException)
        {
            // Non-JSON bodies such as HTML 404 pages.
        }

        return payload.Length <= 500 ? payload : payload[..500];
    }

    private static BillingCustomer? MapCustomer(CustomerResource? resource)
    {
        if (resource is null || resource.Id == 0)
        {
            return null;
        }

        return new BillingCustomer
        {
            Id = resource.Id,
            Reference = resource.Reference,
            Email = resource.Email ?? string.Empty,
            FirstName = resource.FirstName ?? string.Empty,
            LastName = resource.LastName ?? string.Empty
        };
    }

    private static BillingSubscription? MapSubscription(SubscriptionResource? resource)
    {
        if (resource is null || resource.Id == 0)
        {
            return null;
        }

        return new BillingSubscription
        {
            Id = resource.Id,
            State = resource.State ?? string.Empty,
            ProductHandle = resource.Product?.Handle ?? string.Empty,
            ProductName = resource.Product?.Name ?? resource.Product?.Handle ?? string.Empty,
            PriceInCents = resource.ProductPriceInCents != 0
                ? resource.ProductPriceInCents
                : resource.Product?.PriceInCents ?? 0,
            CurrentPeriodEndsAt = ParseTimestamp(resource.CurrentPeriodEndsAt),
            NextBillingAt = ParseTimestamp(resource.NextAssessmentAt) ?? ParseTimestamp(resource.CurrentPeriodEndsAt)
        };
    }

    private static DateTimeOffset? ParseTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    }

    private void EnsureConfigured()
    {
        if (!_options.IsConfigured)
        {
            throw new MaxioNotConfiguredException();
        }
    }
}
