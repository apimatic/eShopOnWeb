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
/// IMaxioBillingService implementation that talks to the Maxio Advanced Billing API.
/// All Maxio calls follow the Billing API docs (Basic auth, *.json endpoints).
/// </summary>
public class MaxioBillingService : IMaxioBillingService
{
    // Subscription states in which the shopper still holds the subscription; re-subscribing
    // to the same plan returns the existing record instead of creating a duplicate.
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "trialing", "assessing", "pending", "past_due",
        "unpaid", "paused", "soft_failure", "on_hold", "awaiting_signup"
    };

    // Serializes subscribe calls per user so a double-click cannot race into two subscriptions.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserLocks = new();

    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(HttpClient httpClient, IOptions<MaxioSettings> settings, ILogger<MaxioBillingService> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
            throw new InvalidOperationException("Maxio:ApiKey is not configured. Set it via user-secrets or the MAXIO_API_KEY environment variable.");
        if (string.IsNullOrWhiteSpace(_settings.BaseUrl) && string.IsNullOrWhiteSpace(_settings.Subdomain))
            throw new InvalidOperationException("Maxio:Subdomain is not configured. Set it via user-secrets or the MAXIO_SITE_SUBDOMAIN environment variable.");
        if (string.IsNullOrWhiteSpace(_settings.ProductFamilyHandle))
            throw new InvalidOperationException("Maxio:ProductFamilyHandle is not configured. Set it via user-secrets or the MAXIO_DEFAULT_PRODUCT_FAMILY environment variable.");
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetSubscriptionPlansAsync(CancellationToken cancellationToken = default)
    {
        // The path accepts the family handle prefixed with "handle:", so no numeric ID is needed.
        var products = await GetAsync<List<MaxioProductEnvelope>>(
            $"product_families/handle:{_settings.ProductFamilyHandle}/products.json", cancellationToken);

        return products
            .Select(p => p.Product)
            .Where(p => p.ArchivedAt is null && !string.IsNullOrEmpty(p.Handle))
            .Select(p => new SubscriptionPlan
            {
                Handle = p.Handle!,
                Name = p.Name,
                Description = p.Description,
                PriceInCents = p.PriceInCents,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit
            })
            .ToList();
    }

    public async Task<SubscriptionDetails> SubscribeAsync(string userId, string email, string planHandle, CancellationToken cancellationToken = default)
    {
        var userLock = UserLocks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await userLock.WaitAsync(cancellationToken);
        try
        {
            var plans = await GetSubscriptionPlansAsync(cancellationToken);
            var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
            if (plan is null)
            {
                throw new PlanNotFoundException(planHandle);
            }

            var customer = await EnsureCustomerAsync(userId, email, cancellationToken);

            var existing = await ListSubscriptionsAsync(customer.Id, cancellationToken);
            var live = existing.FirstOrDefault(s =>
                string.Equals(s.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase) &&
                LiveStates.Contains(s.State));
            if (live is not null)
            {
                _logger.LogInformation("User {UserId} already subscribed to plan {PlanHandle}; returning existing subscription {SubscriptionId}.",
                    userId, plan.Handle, live.Id);
                return Map(live);
            }

            var request = new MaxioCreateSubscriptionRequest
            {
                Subscription = new MaxioCreateSubscription
                {
                    ProductHandle = plan.Handle,
                    CustomerReference = userId,
                    // Remittance bills by invoice instead of auto-charging, so enrollment
                    // succeeds without a payment method on file (plans don't require one).
                    PaymentCollectionMethod = "remittance"
                }
            };
            var created = await PostAsync<MaxioCreateSubscriptionRequest, MaxioSubscriptionEnvelope>(
                "subscriptions.json", request, cancellationToken);

            _logger.LogInformation("Created Maxio subscription {SubscriptionId} for user {UserId} on plan {PlanHandle}.",
                created.Subscription.Id, userId, plan.Handle);
            return Map(created.Subscription);
        }
        finally
        {
            userLock.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDetails>> GetSubscriptionsAsync(string userId, string email, CancellationToken cancellationToken = default)
    {
        var customer = await FindCustomerByReferenceAsync(userId, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDetails>();
        }

        var subscriptions = await ListSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(Map).ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(string userId, string email, CancellationToken cancellationToken)
    {
        var existing = await FindCustomerByReferenceAsync(userId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        // Maxio enforces one customer per reference value, so creation is idempotent by reference.
        var request = new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCreateCustomer
            {
                FirstName = DeriveFirstName(email),
                LastName = "Customer",
                Email = email,
                Reference = userId
            }
        };

        try
        {
            var created = await PostAsync<MaxioCreateCustomerRequest, MaxioCustomerEnvelope>(
                "customers.json", request, cancellationToken);
            _logger.LogInformation("Created Maxio customer {CustomerId} for user {UserId}.", created.Customer.Id, userId);
            return created.Customer;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Lost a race with a concurrent create; the reference is unique so it must exist now.
            var winner = await FindCustomerByReferenceAsync(userId, cancellationToken);
            if (winner is not null)
            {
                return winner;
            }
            throw;
        }
    }

    private async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<MaxioCustomerEnvelope>(cancellationToken: cancellationToken);
        return envelope?.Customer;
    }

    private async Task<List<MaxioSubscription>> ListSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        var envelopes = await GetAsync<List<MaxioSubscriptionEnvelope>>(
            $"customers/{customerId}/subscriptions.json", cancellationToken);
        return envelopes.Select(e => e.Subscription).ToList();
    }

    private async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken);
        return result ?? throw new MaxioApiException(HttpStatusCode.OK, "Empty response body.");
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(string path, TRequest body, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(path, body, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken: cancellationToken);
        return result ?? throw new MaxioApiException(HttpStatusCode.OK, "Empty response body.");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        string message = body;
        try
        {
            var parsed = System.Text.Json.JsonSerializer.Deserialize<MaxioErrorResponse>(body);
            if (parsed?.Errors is { Length: > 0 })
            {
                message = string.Join("; ", parsed.Errors);
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Body wasn't the standard error shape; surface it raw.
        }

        throw new MaxioApiException(response.StatusCode, message);
    }

    private static SubscriptionDetails Map(MaxioSubscription s) => new()
    {
        SubscriptionId = s.Id,
        State = s.State,
        PlanHandle = s.Product?.Handle ?? string.Empty,
        PlanName = s.Product?.Name ?? string.Empty,
        PriceInCents = s.ProductPriceInCents,
        MaxioCustomerId = s.Customer?.Id ?? 0,
        CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
        NextAssessmentAt = s.NextAssessmentAt
    };

    private static string DeriveFirstName(string email)
    {
        var localPart = email.Split('@')[0];
        return string.IsNullOrWhiteSpace(localPart) ? "eShopOnWeb" : localPart;
    }
}

/// <summary>
/// Thrown when a requested plan handle does not exist in the configured product family.
/// </summary>
public class PlanNotFoundException : Exception
{
    public PlanNotFoundException(string planHandle)
        : base($"No subscription plan with handle '{planHandle}' exists in the configured product family.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}
