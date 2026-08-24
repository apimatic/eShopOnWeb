using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// <see cref="ISubscriptionBillingService"/> backed by the Maxio Advanced Billing REST API.
/// Verified against https://developers.maxio.com/http/advanced-billing-api (Basic auth, API key as
/// username with password "x").
/// </summary>
public class MaxioBillingService : ISubscriptionBillingService
{
    // States in which a subscription still occupies the shopper; used to make Subscribe idempotent.
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "ended", "failed_to_create"
    };

    // Serializes subscribe calls per shopper so a double-click cannot create two customers/subscriptions.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscribeLocks = new();

    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(HttpClient httpClient, IOptions<MaxioSettings> settings, ILogger<MaxioBillingService> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        // The product family can be addressed by stable handle ("handle:{handle}") so numeric IDs,
        // which Maxio reassigns on re-seed, are never needed.
        var products = await GetAsync<List<MaxioProductEnvelope>>(
            $"product_families/handle:{Uri.EscapeDataString(_settings.ProductFamilyHandle)}/products.json",
            cancellationToken);

        return products
            .Select(p => p.Product)
            .Where(p => p.ArchivedAt is null)
            .Select(p => new SubscriptionPlan
            {
                Handle = p.Handle,
                Name = p.Name,
                Description = p.Description,
                PriceInCents = p.PriceInCents,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit
            })
            .ToList();
    }

    public async Task<CustomerSubscription> SubscribeAsync(
        string customerReference,
        string email,
        string displayName,
        string productHandle,
        CancellationToken cancellationToken = default)
    {
        var gate = SubscribeLocks.GetOrAdd(customerReference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await GetOrCreateCustomerAsync(customerReference, email, displayName, cancellationToken);

            var existing = await ListSubscriptionsAsync(customer.Id, cancellationToken);
            var current = existing.FirstOrDefault(s =>
                string.Equals(s.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase) &&
                !TerminalStates.Contains(s.State));
            if (current is not null)
            {
                _logger.LogInformation(
                    "Shopper {CustomerReference} already subscribed to {ProductHandle} (subscription {SubscriptionId}); returning existing.",
                    customerReference, productHandle, current.Id);
                return Map(current);
            }

            var request = new MaxioCreateSubscriptionRequest
            {
                Subscription = new MaxioCreateSubscription
                {
                    CustomerId = customer.Id,
                    ProductHandle = productHandle
                }
            };
            var response = await SendAsync(() => _httpClient.PostAsJsonAsync("subscriptions.json", request, cancellationToken));
            await EnsureSuccessAsync(response, cancellationToken);
            var created = await ReadAsync<MaxioSubscriptionEnvelope>(response);
            return Map(created.Subscription);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(
        string customerReference,
        CancellationToken cancellationToken = default)
    {
        var customer = await FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await ListSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(Map).ToList();
    }

    private async Task<MaxioCustomer> GetOrCreateCustomerAsync(
        string customerReference, string email, string displayName, CancellationToken cancellationToken)
    {
        var existing = await FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var request = new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCustomer
            {
                FirstName = displayName,
                LastName = displayName,
                Email = email,
                Reference = customerReference
            }
        };
        var response = await SendAsync(() => _httpClient.PostAsJsonAsync("customers.json", request, cancellationToken));

        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Lost a race with a concurrent create (reference is unique per site) — re-read the winner.
            var winner = await FindCustomerByReferenceAsync(customerReference, cancellationToken);
            if (winner is not null)
            {
                return winner;
            }
            await ThrowForAsync(response, cancellationToken);
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var created = await ReadAsync<MaxioCustomerEnvelope>(response);
        return created.Customer;
    }

    private async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string customerReference, CancellationToken cancellationToken)
    {
        var response = await SendAsync(() =>
            _httpClient.GetAsync($"customers/lookup.json?reference={Uri.EscapeDataString(customerReference)}", cancellationToken));
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await ReadAsync<MaxioCustomerEnvelope>(response);
        return envelope.Customer;
    }

    private async Task<List<MaxioSubscription>> ListSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        var envelopes = await GetAsync<List<MaxioSubscriptionEnvelope>>(
            $"customers/{customerId}/subscriptions.json", cancellationToken);
        return envelopes.Select(e => e.Subscription).ToList();
    }

    private async Task<T> GetAsync<T>(string relativeUrl, CancellationToken cancellationToken)
    {
        var response = await SendAsync(() => _httpClient.GetAsync(relativeUrl, cancellationToken));
        await EnsureSuccessAsync(response, cancellationToken);
        return await ReadAsync<T>(response);
    }

    private async Task<HttpResponseMessage> SendAsync(Func<Task<HttpResponseMessage>> send)
    {
        try
        {
            return await send();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Maxio API call failed: {Message}", ex.Message);
            throw new MaxioBillingException(ex.StatusCode ?? HttpStatusCode.ServiceUnavailable,
                $"Maxio API unreachable: {ex.Message}");
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            await ThrowForAsync(response, cancellationToken);
        }
    }

    private static async Task ThrowForAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string message = $"Maxio API returned {(int)response.StatusCode} ({response.ReasonPhrase}).";
        try
        {
            var error = await response.Content.ReadFromJsonAsync<MaxioErrorResponse>(cancellationToken);
            if (error?.Errors is { Count: > 0 })
            {
                message = string.Join("; ", error.Errors);
            }
        }
        catch (Exception)
        {
            // Body wasn't a JSON error payload; keep the status-line message.
        }

        throw new MaxioBillingException(response.StatusCode, message);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static CustomerSubscription Map(MaxioSubscription subscription) => new()
    {
        SubscriptionId = subscription.Id,
        CustomerId = subscription.Customer?.Id ?? 0,
        State = subscription.State,
        ProductHandle = subscription.Product?.Handle ?? string.Empty,
        ProductName = subscription.Product?.Name ?? string.Empty,
        PriceInCents = subscription.Product?.PriceInCents ?? 0,
        Interval = subscription.Product?.Interval ?? 0,
        IntervalUnit = subscription.Product?.IntervalUnit ?? string.Empty,
        NextBillingDate = subscription.NextAssessmentAt,
        CreatedAt = subscription.CreatedAt
    };
}
