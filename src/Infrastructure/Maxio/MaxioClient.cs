using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Wire;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Typed HttpClient wrapper for the Maxio Advanced Billing API. Every wire-format detail
/// (auth scheme, endpoint shapes, error bodies) comes from the maxio-docs MCP server's
/// Billing API reference - see the OpenAPI spec under /openapi/api-reference/openapi.yaml.
/// </summary>
public class MaxioClient : IMaxioClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly IAppLogger<MaxioClient> _logger;

    public MaxioClient(HttpClient httpClient, IOptions<MaxioOptions> options, IAppLogger<MaxioClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        if (_options.IsConfigured)
        {
            _httpClient.BaseAddress = new Uri(_options.ResolveBaseUrl());
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            var basicAuthValue = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ApiKey}:x"));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basicAuthValue);
        }
    }

    public async Task<IReadOnlyList<MaxioPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var familyHandle = "handle:" + Uri.EscapeDataString(_options.ProductFamilyHandle);
        using var response = await _httpClient.GetAsync($"product_families/{familyHandle}/products.json?per_page=200", cancellationToken);
        await EnsureSuccessAsync(response, "listing subscription plans", cancellationToken);

        var wrappers = await response.Content.ReadFromJsonAsync<List<ProductEnvelope>>(JsonOptions, cancellationToken) ?? new();

        return wrappers
            .Select(w => w.Product)
            .Where(p => p is not null && p.ArchivedAt is null)
            .Select(p => new MaxioPlan
            {
                Id = p!.Id,
                Handle = p.Handle ?? string.Empty,
                Name = p.Name ?? string.Empty,
                Description = p.Description,
                PriceInCents = p.PriceInCents,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit ?? string.Empty,
                RequiresPaymentMethod = p.RequireCreditCard
            })
            .ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        return await LookupCustomerByReferenceAsync(reference, cancellationToken);
    }

    public async Task<MaxioCustomer> EnsureCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var existing = await LookupCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var body = new CreateCustomerBody
        {
            Customer = new CreateCustomerAttributes
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference
            }
        };

        using var response = await _httpClient.PostAsJsonAsync("customers.json", body, JsonOptions, cancellationToken);

        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Reference-uniqueness race: another concurrent request created the customer
            // between our lookup and this create call. Maxio guarantees only one customer
            // per reference, so recover by re-fetching rather than failing.
            var recovered = await LookupCustomerByReferenceAsync(reference, cancellationToken);
            if (recovered is not null)
            {
                return recovered;
            }

            var rawError = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new MaxioApiException($"Maxio rejected customer creation: {MaxioErrorParser.Extract(rawError)}", (int)response.StatusCode);
        }

        await EnsureSuccessAsync(response, "creating a Maxio customer", cancellationToken);

        var envelope = await response.Content.ReadFromJsonAsync<CustomerEnvelope>(JsonOptions, cancellationToken);
        if (envelope?.Customer is null)
        {
            throw new MaxioApiException("Maxio returned an empty customer response.");
        }

        return ToCustomer(envelope.Customer);
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        using var response = await _httpClient.GetAsync($"customers/{customerId}/subscriptions.json", cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return Array.Empty<MaxioSubscription>();
        }

        await EnsureSuccessAsync(response, "listing customer subscriptions", cancellationToken);

        var wrappers = await response.Content.ReadFromJsonAsync<List<SubscriptionEnvelope>>(JsonOptions, cancellationToken) ?? new();
        return wrappers
            .Select(w => w.Subscription)
            .Where(s => s is not null)
            .Select(s => ToSubscription(s!, customerId))
            .ToList();
    }

    public async Task<MaxioSubscription> SubscribeAsync(long customerId, string planHandle, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var uniquenessToken = DeriveUniquenessToken(customerId, planHandle);
        var body = new CreateSubscriptionBody
        {
            Subscription = new CreateSubscriptionAttributes
            {
                ProductHandle = planHandle,
                CustomerId = customerId
            },
            UniquenessToken = uniquenessToken
        };

        using var response = await _httpClient.PostAsJsonAsync("subscriptions.json", body, JsonOptions, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            // Duplicate submission of the same uniqueness_token: the original request either
            // already succeeded or is in flight. Recover the resulting subscription instead
            // of surfacing an error for what is, from the caller's perspective, a retry.
            var subscriptions = await ListCustomerSubscriptionsAsync(customerId, cancellationToken);
            var recovered = subscriptions
                .Where(s => string.Equals(s.PlanHandle, planHandle, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(s => s.Id)
                .FirstOrDefault();

            if (recovered is not null)
            {
                return recovered;
            }

            throw new MaxioApiException("Maxio reported a duplicate subscription request, but no matching subscription could be found. Please retry.", (int)response.StatusCode);
        }

        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var rawError = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new MaxioApiException($"Maxio rejected the subscription request: {MaxioErrorParser.Extract(rawError)}", (int)response.StatusCode);
        }

        await EnsureSuccessAsync(response, "creating a subscription", cancellationToken);

        var envelope = await response.Content.ReadFromJsonAsync<SubscriptionEnvelope>(JsonOptions, cancellationToken);
        if (envelope?.Subscription is null)
        {
            throw new MaxioApiException("Maxio returned an empty subscription response.");
        }

        return ToSubscription(envelope.Subscription, customerId);
    }

    private async Task<MaxioCustomer?> LookupCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var encodedReference = Uri.EscapeDataString(reference);
        using var response = await _httpClient.GetAsync($"customers/lookup.json?reference={encodedReference}", cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, "looking up a Maxio customer", cancellationToken);

        var envelope = await response.Content.ReadFromJsonAsync<CustomerEnvelope>(JsonOptions, cancellationToken);
        return envelope?.Customer is null ? null : ToCustomer(envelope.Customer);
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, string action, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var rawBody = await response.Content.ReadAsStringAsync(cancellationToken);
        var message = $"Maxio request failed while {action} (HTTP {(int)response.StatusCode}): {MaxioErrorParser.Extract(rawBody)}";
        _logger.LogWarning(message);
        throw new MaxioApiException(message, (int)response.StatusCode);
    }

    private void EnsureConfigured()
    {
        if (!_options.IsConfigured)
        {
            throw new MaxioApiException(
                "Maxio is not configured. Set Maxio:ApiKey and Maxio:Subdomain (or Maxio:BaseUrl) via user-secrets/environment variables.");
        }
    }

    /// <summary>
    /// Deterministic per (customer, plan) uniqueness token: concurrent/duplicated subscribe
    /// requests for the same customer+plan collapse into a single Maxio subscription instead
    /// of racing to create two.
    /// </summary>
    private static string DeriveUniquenessToken(long customerId, string planHandle)
    {
        var seed = $"eshoponweb-subscribe:{customerId}:{planHandle}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        return Convert.ToHexString(hash);
    }

    private static MaxioCustomer ToCustomer(CustomerWire wire) => new()
    {
        Id = wire.Id,
        Reference = wire.Reference ?? string.Empty,
        Email = wire.Email ?? string.Empty,
        FirstName = wire.FirstName ?? string.Empty,
        LastName = wire.LastName ?? string.Empty
    };

    private static MaxioSubscription ToSubscription(SubscriptionWire wire, long fallbackCustomerId) => new()
    {
        Id = wire.Id,
        CustomerId = wire.Customer?.Id ?? fallbackCustomerId,
        State = wire.State ?? string.Empty,
        PlanHandle = wire.Product?.Handle ?? string.Empty,
        PlanName = wire.Product?.Name ?? string.Empty,
        PriceInCents = wire.Product?.PriceInCents ?? 0,
        Interval = wire.Product?.Interval ?? 0,
        IntervalUnit = wire.Product?.IntervalUnit ?? string.Empty,
        CurrentPeriodEndsAt = wire.CurrentPeriodEndsAt,
        NextBillingAt = wire.NextAssessmentAt
    };
}
