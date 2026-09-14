using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Services.Maxio.Wire;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Maxio;

/// <summary>
/// HTTP client for Maxio Advanced Billing (https://developers.maxio.com/http/advanced-billing-api).
/// Authenticates with HTTP Basic Auth (API key as username, literal "x" as password) against
/// https://{subdomain}.chargify.com, or Maxio:BaseUrl when that override is configured.
/// </summary>
public class MaxioClient : IMaxioClient
{
    // Subscription states that mean "this customer is already enrolled" for idempotency purposes.
    // Only a canceled/expired subscription should allow a fresh subscription to the same plan.
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled",
        "expired",
    };

    // Guards the read-check-then-write sequence in SubscribeAsync per customer reference, so a
    // double-click from the same process can't race past the "does a subscription already exist"
    // check and create two subscriptions. Maxio's unique-reference constraint on customer creation
    // is the backstop for races across processes/instances.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscribeLocks = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;

    public MaxioClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public static Uri ResolveBaseAddress(MaxioOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            return new Uri(options.BaseUrl.TrimEnd('/') + "/");
        }

        if (string.IsNullOrWhiteSpace(options.Subdomain))
        {
            throw new InvalidOperationException(
                "Maxio configuration is missing: set either Maxio:BaseUrl or Maxio:Subdomain.");
        }

        return new Uri($"https://{options.Subdomain}.chargify.com/");
    }

    public static string BuildBasicAuthHeaderValue(string apiKey) =>
        Convert.ToBase64String(Encoding.ASCII.GetBytes($"{apiKey}:x"));

    public async Task<IReadOnlyList<MaxioPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw new InvalidOperationException("Maxio:ProductFamilyHandle is not configured.");
        }

        var path = $"product_families/handle:{Uri.EscapeDataString(_options.ProductFamilyHandle)}/products.json";
        var envelopes = await SendAsync<List<ProductEnvelope>>(HttpMethod.Get, path, body: null, cancellationToken)
            ?? new List<ProductEnvelope>();

        return envelopes
            .Select(e => e.Product)
            .Where(p => p is not null && p.ArchivedAt is null)
            .Select(p => new MaxioPlan
            {
                Handle = p!.Handle ?? string.Empty,
                Name = p.Name ?? p.Handle ?? string.Empty,
                Description = p.Description,
                PriceInCents = p.PriceInCents,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit ?? string.Empty,
            })
            .ToList();
    }

    public async Task<MaxioSubscribeResult> SubscribeAsync(
        string customerReference,
        string email,
        string firstName,
        string lastName,
        string planHandle,
        CancellationToken cancellationToken = default)
    {
        var lockHandle = SubscribeLocks.GetOrAdd(customerReference, _ => new SemaphoreSlim(1, 1));
        await lockHandle.WaitAsync(cancellationToken);
        try
        {
            var customer = await GetOrCreateCustomerAsync(customerReference, email, firstName, lastName, cancellationToken);

            var existingSubscriptions = await ListSubscriptionsForCustomerIdAsync(customer.Id, cancellationToken);
            var existing = existingSubscriptions.FirstOrDefault(s =>
                string.Equals(s.PlanHandle, planHandle, StringComparison.OrdinalIgnoreCase) &&
                !TerminalStates.Contains(s.State));

            if (existing is not null)
            {
                return new MaxioSubscribeResult { Subscription = existing, WasNewlyCreated = false };
            }

            var createBody = new CreateSubscriptionEnvelope
            {
                Subscription = new CreateSubscriptionWire
                {
                    ProductHandle = planHandle,
                    CustomerId = customer.Id,
                },
            };

            var created = await SendAsync<SubscriptionEnvelope>(
                HttpMethod.Post, "subscriptions.json", createBody, cancellationToken);

            if (created?.Subscription is null)
            {
                throw new MaxioApiException(502, new[] { "Maxio returned an empty subscription response." });
            }

            return new MaxioSubscribeResult { Subscription = ToDomain(created.Subscription), WasNewlyCreated = true };
        }
        finally
        {
            lockHandle.Release();
        }
    }

    public async Task<IReadOnlyList<MaxioSubscription>> GetSubscriptionsForCustomerAsync(
        string customerReference, CancellationToken cancellationToken = default)
    {
        var customer = await FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<MaxioSubscription>();
        }

        return await ListSubscriptionsForCustomerIdAsync(customer.Id, cancellationToken);
    }

    private async Task<CustomerWire> GetOrCreateCustomerAsync(
        string reference, string email, string firstName, string lastName, CancellationToken cancellationToken)
    {
        var existing = await FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var body = new CreateCustomerEnvelope
        {
            Customer = new CreateCustomerWire
            {
                Reference = reference,
                Email = email,
                FirstName = firstName,
                LastName = lastName,
            },
        };

        try
        {
            var created = await SendAsync<CustomerEnvelope>(HttpMethod.Post, "customers.json", body, cancellationToken);
            if (created?.Customer is null)
            {
                throw new MaxioApiException(502, new[] { "Maxio returned an empty customer response." });
            }

            return created.Customer;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 422)
        {
            // Another request created this customer between our lookup and our create
            // (e.g. a genuine double-click that beat the in-process lock, or a second
            // instance of this service). The reference is unique in Maxio, so re-fetch it.
            var raceWinner = await FindCustomerByReferenceAsync(reference, cancellationToken);
            if (raceWinner is not null)
            {
                return raceWinner;
            }

            throw;
        }
    }

    private async Task<CustomerWire?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var envelope = await SendAsync<CustomerEnvelope>(HttpMethod.Get, path, body: null, cancellationToken, treatNotFoundAsNull: true);
        return envelope?.Customer;
    }

    private async Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsForCustomerIdAsync(
        int customerId, CancellationToken cancellationToken)
    {
        var path = $"subscriptions.json?customer_id={customerId}";
        var envelopes = await SendAsync<List<SubscriptionEnvelope>>(HttpMethod.Get, path, body: null, cancellationToken)
            ?? new List<SubscriptionEnvelope>();

        return envelopes
            .Select(e => e.Subscription)
            .Where(s => s is not null)
            .Select(s => ToDomain(s!))
            .ToList();
    }

    private static MaxioSubscription ToDomain(SubscriptionWire wire) => new()
    {
        Id = wire.Id,
        State = wire.State ?? string.Empty,
        PlanHandle = wire.Product?.Handle ?? string.Empty,
        PlanName = wire.Product?.Name ?? string.Empty,
        PriceInCents = wire.Product?.PriceInCents ?? 0,
        CurrentPeriodEndsAt = wire.CurrentPeriodEndsAt,
        NextAssessmentAt = wire.NextAssessmentAt,
        CreatedAt = wire.CreatedAt,
        ActivatedAt = wire.ActivatedAt,
    };

    private async Task<T?> SendAsync<T>(
        HttpMethod method,
        string relativeUri,
        object? body,
        CancellationToken cancellationToken,
        bool treatNotFoundAsNull = false)
        where T : class
    {
        using var request = new HttpRequestMessage(method, relativeUri);
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
        catch (HttpRequestException ex)
        {
            throw new MaxioApiException($"Could not reach Maxio Advanced Billing: {ex.Message}", ex);
        }

        using (response)
        {
            if (treatNotFoundAsNull && response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                var errors = await ExtractErrorsAsync(response, cancellationToken);
                throw new MaxioApiException((int)response.StatusCode, errors);
            }

            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
        }
    }

    private static async Task<IReadOnlyList<string>> ExtractErrorsAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(payload))
            {
                return new[] { response.ReasonPhrase ?? "Unknown error" };
            }

            var parsed = JsonSerializer.Deserialize<MaxioErrorResponse>(payload, JsonOptions);
            if (parsed?.Errors is { Length: > 0 })
            {
                return parsed.Errors;
            }

            if (!string.IsNullOrWhiteSpace(parsed?.Error))
            {
                return new[] { parsed!.Error! };
            }

            return new[] { payload };
        }
        catch (JsonException)
        {
            return new[] { response.ReasonPhrase ?? "Unknown error" };
        }
    }
}
