using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// <see cref="IMaxioClient"/> implementation backed by Maxio Advanced Billing's REST API.
/// The <see cref="HttpClient"/> injected here is expected to already have its base address
/// and Basic-auth Authorization header configured (see Program.cs's AddHttpClient wiring).
/// </summary>
public class MaxioClient : IMaxioClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // A site's billing architecture (and therefore which payment_collection_method values it
    // accepts) doesn't change at runtime, so this is cached for the life of the process rather
    // than re-fetched on every subscribe call. Static because AddHttpClient<T> hands out a new
    // MaxioClient instance per resolution.
    private static string? _cachedPaymentCollectionMethod;
    private static readonly SemaphoreSlim PaymentCollectionMethodLock = new(1, 1);

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly IAppLogger<MaxioClient> _logger;

    public MaxioClient(HttpClient httpClient, IOptions<MaxioOptions> options, IAppLogger<MaxioClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<CustomerEnvelope>(SerializerOptions, cancellationToken);
        return envelope?.Customer is null ? null : Map(envelope.Customer);
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken cancellationToken = default)
    {
        var body = new CreateCustomerRequestBody
        {
            Customer = new CreateCustomerAttributes
            {
                Reference = reference,
                Email = email,
                FirstName = firstName,
                LastName = lastName
            }
        };

        var response = await _httpClient.PostAsJsonAsync("customers.json", body, SerializerOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<CustomerEnvelope>(SerializerOptions, cancellationToken);
        if (envelope?.Customer is null)
        {
            throw new MaxioApiException((int)response.StatusCode, "Maxio returned an empty customer payload.", Array.Empty<string>());
        }

        return Map(envelope.Customer);
    }

    public async Task<IReadOnlyList<MaxioPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"product_families/handle:{Uri.EscapeDataString(_options.ProductFamilyHandle)}/products.json", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var envelopes = await response.Content.ReadFromJsonAsync<List<ProductEnvelope>>(SerializerOptions, cancellationToken) ?? new();

        return envelopes
            .Select(e => e.Product)
            .Where(p => p is not null && p.ArchivedAt is null)
            .Select(p => Map(p!))
            .ToList();
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"customers/{customerId}/subscriptions.json", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var envelopes = await response.Content.ReadFromJsonAsync<List<SubscriptionEnvelope>>(SerializerOptions, cancellationToken) ?? new();

        return envelopes
            .Select(e => e.Subscription)
            .Where(s => s is not null)
            .Select(s => Map(s!))
            .ToList();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string planHandle, CancellationToken cancellationToken = default)
    {
        var body = new CreateSubscriptionRequestBody
        {
            Subscription = new CreateSubscriptionAttributes
            {
                CustomerId = customerId,
                ProductHandle = planHandle,
                PaymentCollectionMethod = await ResolvePaymentCollectionMethodAsync(cancellationToken)
            }
        };

        var response = await _httpClient.PostAsJsonAsync("subscriptions.json", body, SerializerOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<SubscriptionEnvelope>(SerializerOptions, cancellationToken);
        if (envelope?.Subscription is null)
        {
            throw new MaxioApiException((int)response.StatusCode, "Maxio returned an empty subscription payload.", Array.Empty<string>());
        }

        return Map(envelope.Subscription);
    }

    private async Task<string> ResolvePaymentCollectionMethodAsync(CancellationToken cancellationToken)
    {
        if (_cachedPaymentCollectionMethod is not null)
        {
            return _cachedPaymentCollectionMethod;
        }

        await PaymentCollectionMethodLock.WaitAsync(cancellationToken);
        try
        {
            if (_cachedPaymentCollectionMethod is not null)
            {
                return _cachedPaymentCollectionMethod;
            }

            // The site's default collection method is "automatic" (an immediate card charge),
            // which fails signup for our no-payment-method-required plans. Relationship
            // Invoicing sites accept "remittance" for a card-free signup that bills later;
            // legacy statement-based sites use "invoice" for the same purpose.
            var response = await _httpClient.GetAsync("site.json", cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);
            var envelope = await response.Content.ReadFromJsonAsync<SiteEnvelope>(SerializerOptions, cancellationToken);
            _cachedPaymentCollectionMethod = envelope?.Site?.RelationshipInvoicingEnabled == true ? "remittance" : "invoice";
            return _cachedPaymentCollectionMethod;
        }
        finally
        {
            PaymentCollectionMethodLock.Release();
        }
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var errors = ExtractErrors(body);
        var message = errors.Count > 0
            ? string.Join(" ", errors)
            : $"Maxio API request failed with status {(int)response.StatusCode} ({response.ReasonPhrase}).";

        _logger.LogWarning("Maxio API call to {0} failed with {1}: {2}", response.RequestMessage?.RequestUri?.ToString() ?? "(unknown)", (int)response.StatusCode, message);

        throw new MaxioApiException((int)response.StatusCode, message, errors);
    }

    private static List<string> ExtractErrors(string body)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(body))
        {
            return errors;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("errors", out var errorsElement))
            {
                return errors;
            }

            switch (errorsElement.ValueKind)
            {
                case JsonValueKind.Array:
                    errors.AddRange(errorsElement.EnumerateArray().Select(e => e.ToString()));
                    break;
                case JsonValueKind.Object:
                    foreach (var property in errorsElement.EnumerateObject())
                    {
                        if (property.Value.ValueKind == JsonValueKind.Array)
                        {
                            errors.AddRange(property.Value.EnumerateArray().Select(v => $"{property.Name}: {v}"));
                        }
                        else
                        {
                            errors.Add($"{property.Name}: {property.Value}");
                        }
                    }
                    break;
                case JsonValueKind.String:
                    errors.Add(errorsElement.GetString() ?? string.Empty);
                    break;
            }
        }
        catch (JsonException)
        {
            // Not a JSON error body (e.g. an HTML error page from an intermediary); ignore and
            // fall back to the generic status-code message built by the caller.
        }

        return errors;
    }

    private static MaxioCustomer Map(CustomerWire wire) => new()
    {
        Id = wire.Id,
        Reference = wire.Reference ?? string.Empty,
        Email = wire.Email ?? string.Empty,
        FirstName = wire.FirstName ?? string.Empty,
        LastName = wire.LastName ?? string.Empty
    };

    private static MaxioPlan Map(ProductWire wire) => new()
    {
        Id = wire.Id,
        Handle = wire.Handle ?? string.Empty,
        Name = wire.Name ?? string.Empty,
        PriceInCents = wire.PriceInCents,
        Interval = wire.Interval,
        IntervalUnit = wire.IntervalUnit ?? string.Empty,
        RequireCreditCard = wire.RequireCreditCard
    };

    private static MaxioSubscription Map(SubscriptionWire wire) => new()
    {
        Id = wire.Id,
        State = wire.State ?? string.Empty,
        PlanHandle = wire.Product?.Handle ?? string.Empty,
        PlanName = wire.Product?.Name ?? string.Empty,
        PriceInCents = wire.Product?.PriceInCents ?? 0,
        NextBillingAt = wire.NextAssessmentAt ?? wire.CurrentPeriodEndsAt,
        CurrentPeriodEndsAt = wire.CurrentPeriodEndsAt,
        CreatedAt = wire.CreatedAt
    };
}
