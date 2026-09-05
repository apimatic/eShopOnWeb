using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Wire;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Talks to the Maxio Advanced Billing per-site REST API (https://{subdomain}.chargify.com),
/// authenticated with HTTP Basic Auth (API key as username, literal "x" as password).
/// Confirmed against Maxio/Chargify's official API docs and the current ab-dotnet-sdk reference
/// (product_families, products, customers, subscriptions resources).
/// </summary>
public class MaxioClient : IMaxioClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    // Product family id rarely changes; cache it per (subdomain, handle) to avoid resolving it
    // on every call. MaxioClient itself is a transient typed client, so this must be static.
    private static readonly ConcurrentDictionary<string, (long FamilyId, DateTimeOffset ExpiresAtUtc)> FamilyIdCache = new();
    private static readonly TimeSpan FamilyIdCacheDuration = TimeSpan.FromMinutes(5);

    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly IAppLogger<MaxioClient> _logger;

    public MaxioClient(HttpClient httpClient, IOptions<MaxioSettings> options, IAppLogger<MaxioClient> logger)
    {
        _httpClient = httpClient;
        _settings = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MaxioPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var familyId = await ResolveProductFamilyIdAsync(cancellationToken);
        var products = await SendAsync<List<ProductEnvelope>>(HttpMethod.Get, $"product_families/{familyId}/products.json", null, cancellationToken);

        return products
            .Select(e => e.Product)
            .Where(p => p is not null && p.ArchivedAt is null)
            .Select(p => new MaxioPlan
            {
                Id = p!.Id,
                Handle = p.Handle ?? string.Empty,
                Name = p.Name ?? string.Empty,
                PriceInCents = p.PriceInCents,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit ?? string.Empty
            })
            .ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await _httpClient.GetAsync(path, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await ReadAsync<CustomerEnvelope>(response, cancellationToken);
        return envelope.Customer is null ? null : ToCustomer(envelope.Customer);
    }

    public async Task<MaxioCustomer> FindOrCreateCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken cancellationToken = default)
    {
        var existing = await FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            var envelope = await SendAsync<CustomerEnvelope>(HttpMethod.Post, "customers.json",
                new CreateCustomerRequestEnvelope
                {
                    Customer = new CreateCustomerRequestWire
                    {
                        Reference = reference,
                        Email = email,
                        FirstName = firstName,
                        LastName = lastName
                    }
                },
                cancellationToken);

            return ToCustomer(envelope.Customer!);
        }
        catch (MaxioApiException)
        {
            // A concurrent request (e.g. a double-click) may have created the customer for this
            // reference between our lookup and this create call. Maxio enforces uniqueness on
            // reference, so re-fetch instead of failing the request.
            var racedCustomer = await FindCustomerByReferenceAsync(reference, cancellationToken);
            if (racedCustomer is not null)
            {
                return racedCustomer;
            }

            throw;
        }
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(long customerId, string planHandle, CancellationToken cancellationToken = default)
    {
        var envelope = await SendAsync<SubscriptionEnvelope>(HttpMethod.Post, "subscriptions.json",
            new CreateSubscriptionRequestEnvelope
            {
                Subscription = new CreateSubscriptionRequestWire
                {
                    ProductHandle = planHandle,
                    CustomerId = customerId
                }
            },
            cancellationToken);

        return ToSubscription(envelope.Subscription!);
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        var envelopes = await SendAsync<List<SubscriptionEnvelope>>(HttpMethod.Get, $"customers/{customerId}/subscriptions.json", null, cancellationToken);
        return envelopes
            .Where(e => e.Subscription is not null)
            .Select(e => ToSubscription(e.Subscription!))
            .ToList();
    }

    private async Task<long> ResolveProductFamilyIdAsync(CancellationToken cancellationToken)
    {
        var cacheKey = $"{_settings.Subdomain}::{_settings.ProductFamilyHandle}";
        if (FamilyIdCache.TryGetValue(cacheKey, out var cached) && cached.ExpiresAtUtc > DateTimeOffset.UtcNow)
        {
            return cached.FamilyId;
        }

        var families = await SendAsync<List<ProductFamilyEnvelope>>(HttpMethod.Get, "product_families.json?per_page=200", null, cancellationToken);
        var match = families
            .Select(e => e.ProductFamily)
            .FirstOrDefault(f => f is not null && string.Equals(f.Handle, _settings.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            throw new MaxioConfigurationException(
                $"No Maxio product family with handle '{_settings.ProductFamilyHandle}' was found on site '{_settings.Subdomain}'. Check Maxio:ProductFamilyHandle and Maxio:Subdomain.");
        }

        FamilyIdCache[cacheKey] = (match.Id, DateTimeOffset.UtcNow.Add(FamilyIdCacheDuration));
        return match.Id;
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await ReadAsync<T>(response, cancellationToken);
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        var errors = new List<string>();
        try
        {
            var parsed = JsonSerializer.Deserialize<ErrorsEnvelope>(content, JsonOptions);
            if (parsed?.Errors is not null)
            {
                errors.AddRange(parsed.Errors);
            }
        }
        catch (JsonException)
        {
            // Body wasn't the expected {"errors": [...]} shape; fall through to raw content below.
        }

        if (errors.Count == 0 && !string.IsNullOrWhiteSpace(content))
        {
            errors.Add(content);
        }

        _logger.LogWarning("Maxio API call to {0} {1} failed with {2}: {3}", response.RequestMessage?.Method.Method ?? "?", response.RequestMessage?.RequestUri?.ToString() ?? "?", (int)response.StatusCode, string.Join("; ", errors));
        throw new MaxioApiException((int)response.StatusCode, errors);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var result = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
        return result ?? throw new MaxioApiException((int)response.StatusCode, new List<string> { "Maxio returned an empty response body." });
    }

    private static MaxioCustomer ToCustomer(CustomerWire wire) => new()
    {
        Id = wire.Id,
        Reference = wire.Reference ?? string.Empty,
        Email = wire.Email ?? string.Empty
    };

    private static MaxioSubscription ToSubscription(SubscriptionWire wire) => new()
    {
        Id = wire.Id,
        State = wire.State ?? string.Empty,
        CustomerId = wire.Customer?.Id ?? 0,
        ProductHandle = wire.Product?.Handle ?? string.Empty,
        ProductName = wire.Product?.Name ?? string.Empty,
        PriceInCents = wire.Product?.PriceInCents ?? 0,
        Interval = wire.Product?.Interval ?? 0,
        IntervalUnit = wire.Product?.IntervalUnit ?? string.Empty,
        NextAssessmentAt = wire.NextAssessmentAt,
        CurrentPeriodEndsAt = wire.CurrentPeriodEndsAt
    };
}
