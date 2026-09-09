using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// <see cref="ISubscriptionBillingService"/> implementation over the Maxio Advanced Billing
/// HTTP API. Registered as a typed <see cref="HttpClient"/> whose base address and Basic-auth
/// header are configured at composition time.
/// </summary>
public class MaxioBillingService : ISubscriptionBillingService
{
    // Subscription states that mean the user does NOT have a live subscription to a plan, so a
    // fresh subscribe should proceed rather than being treated as an idempotent replay.
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create", "trial_ended", "unpaid"
    };

    private static readonly CultureInfo PriceCulture = CultureInfo.GetCultureInfo("en-US");

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly MaxioSubscriptionCoordinator _coordinator;
    private readonly IAppLogger<MaxioBillingService> _logger;

    public MaxioBillingService(
        HttpClient httpClient,
        MaxioSettings settings,
        MaxioSubscriptionCoordinator coordinator,
        IAppLogger<MaxioBillingService> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _coordinator = coordinator;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await GetFamilyProductsAsync(cancellationToken).ConfigureAwait(false);

        return products
            .Where(p => p.ArchivedAt is null && !string.IsNullOrEmpty(p.Handle))
            .OrderBy(p => p.PriceInCents)
            .Select(MapPlan)
            .ToList();
    }

    public async Task<CustomerSubscription> SubscribeAsync(
        BillingCustomer customer,
        string planHandle,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new SubscriptionValidationException("A plan handle is required to subscribe.");
        }

        // Validate the requested plan is a real, active plan in the configured family before we
        // create anything. This gives a clean 422 for unknown plans and prevents subscribing to
        // arbitrary products outside the storefront's catalog.
        var products = await GetFamilyProductsAsync(cancellationToken).ConfigureAwait(false);
        var plan = products.FirstOrDefault(p =>
            p.ArchivedAt is null &&
            string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));

        if (plan is null)
        {
            throw new SubscriptionValidationException(
                $"Unknown or unavailable plan '{planHandle}'. Choose a plan from GET /api/subscription-plans.");
        }

        // Serialize per-user so a double-click cannot create two customers/subscriptions.
        using (await _coordinator.LockAsync(customer.Reference, cancellationToken).ConfigureAwait(false))
        {
            var maxioCustomer = await EnsureCustomerAsync(customer, cancellationToken).ConfigureAwait(false);

            var existing = await FindLiveSubscriptionAsync(maxioCustomer.Id, plan.Handle!, cancellationToken)
                .ConfigureAwait(false);
            if (existing is not null)
            {
                _logger.LogInformation(
                    $"Maxio: reusing existing subscription {existing.Id} ({existing.State}) for customer {maxioCustomer.Id} on plan '{plan.Handle}'.");
                return MapSubscription(existing, wasCreated: false);
            }

            var created = await CreateSubscriptionAsync(plan.Handle!, maxioCustomer.Id, cancellationToken)
                .ConfigureAwait(false);
            _logger.LogInformation(
                $"Maxio: created subscription {created.Id} ({created.State}) for customer {maxioCustomer.Id} on plan '{plan.Handle}'.");
            return MapSubscription(created, wasCreated: true);
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsForUserAsync(
        string customerReference,
        CancellationToken cancellationToken = default)
    {
        var customer = await LookupCustomerAsync(customerReference, cancellationToken).ConfigureAwait(false);
        if (customer is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await ListCustomerSubscriptionsAsync(customer.Id, cancellationToken)
            .ConfigureAwait(false);

        return subscriptions
            .OrderByDescending(s => s.CreatedAt ?? DateTimeOffset.MinValue)
            .Select(s => MapSubscription(s, wasCreated: false))
            .ToList();
    }

    // --- Maxio API operations ---------------------------------------------------------------

    private async Task<List<MaxioProduct>> GetFamilyProductsAsync(CancellationToken cancellationToken)
    {
        var family = await GetProductFamilyAsync(cancellationToken).ConfigureAwait(false);

        using var response = await _httpClient
            .GetAsync($"product_families/{family.Id}/products.json?per_page=200", cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            await ThrowForErrorAsync(response, "list products", cancellationToken).ConfigureAwait(false);
        }

        var wrappers = await ReadJsonAsync<List<MaxioProductWrapper>>(response, "list products", cancellationToken)
            .ConfigureAwait(false);

        return wrappers
            .Select(w => w.Product)
            .Where(p => p is not null)
            .Select(p => p!)
            .ToList();
    }

    private async Task<MaxioProductFamily> GetProductFamilyAsync(CancellationToken cancellationToken)
    {
        using var response = await _httpClient
            .GetAsync($"product_families/handle:{Uri.EscapeDataString(_settings.ProductFamilyHandle)}.json", cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new SubscriptionBillingException(
                $"Maxio product family '{_settings.ProductFamilyHandle}' was not found on site '{_settings.Subdomain}'.");
        }

        if (!response.IsSuccessStatusCode)
        {
            await ThrowForErrorAsync(response, "read product family", cancellationToken).ConfigureAwait(false);
        }

        var wrapper = await ReadJsonAsync<MaxioProductFamilyWrapper>(response, "read product family", cancellationToken)
            .ConfigureAwait(false);

        if (wrapper.ProductFamily is null)
        {
            throw new SubscriptionBillingException("Maxio returned an empty product family response.");
        }

        return wrapper.ProductFamily;
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(BillingCustomer customer, CancellationToken cancellationToken)
    {
        var existing = await LookupCustomerAsync(customer.Reference, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        var request = new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCustomerAttributes
            {
                FirstName = customer.FirstName,
                LastName = customer.LastName,
                Email = customer.Email,
                Reference = customer.Reference
            }
        };

        using var response = await _httpClient
            .PostAsJsonAsync("customers.json", request, JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        // Lost a race with a concurrent creator (out-of-process): the reference is now taken.
        // Re-read and use the customer that won.
        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var reread = await LookupCustomerAsync(customer.Reference, cancellationToken).ConfigureAwait(false);
            if (reread is not null)
            {
                return reread;
            }

            await ThrowForErrorAsync(response, "create customer", cancellationToken).ConfigureAwait(false);
        }

        if (!response.IsSuccessStatusCode)
        {
            await ThrowForErrorAsync(response, "create customer", cancellationToken).ConfigureAwait(false);
        }

        var wrapper = await ReadJsonAsync<MaxioCustomerWrapper>(response, "create customer", cancellationToken)
            .ConfigureAwait(false);

        if (wrapper.Customer is null)
        {
            throw new SubscriptionBillingException("Maxio returned an empty customer response.");
        }

        _logger.LogInformation($"Maxio: created customer {wrapper.Customer.Id} for reference '{customer.Reference}'.");
        return wrapper.Customer;
    }

    private async Task<MaxioCustomer?> LookupCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        using var response = await _httpClient
            .GetAsync($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            await ThrowForErrorAsync(response, "look up customer", cancellationToken).ConfigureAwait(false);
        }

        var wrapper = await ReadJsonAsync<MaxioCustomerWrapper>(response, "look up customer", cancellationToken)
            .ConfigureAwait(false);
        return wrapper.Customer;
    }

    private async Task<MaxioSubscription?> FindLiveSubscriptionAsync(
        long customerId,
        string planHandle,
        CancellationToken cancellationToken)
    {
        var subscriptions = await ListCustomerSubscriptionsAsync(customerId, cancellationToken).ConfigureAwait(false);

        return subscriptions.FirstOrDefault(s =>
            s.Product is not null &&
            string.Equals(s.Product.Handle, planHandle, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrEmpty(s.State) &&
            !TerminalStates.Contains(s.State!));
    }

    private async Task<List<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        long customerId,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient
            .GetAsync($"customers/{customerId}/subscriptions.json?per_page=200", cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            await ThrowForErrorAsync(response, "list customer subscriptions", cancellationToken).ConfigureAwait(false);
        }

        var wrappers = await ReadJsonAsync<List<MaxioSubscriptionWrapper>>(response, "list customer subscriptions", cancellationToken)
            .ConfigureAwait(false);

        return wrappers
            .Select(w => w.Subscription)
            .Where(s => s is not null)
            .Select(s => s!)
            .ToList();
    }

    private async Task<MaxioSubscription> CreateSubscriptionAsync(
        string planHandle,
        long customerId,
        CancellationToken cancellationToken)
    {
        var request = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioSubscriptionAttributes
            {
                ProductHandle = planHandle,
                CustomerId = customerId,
                PaymentCollectionMethod = _settings.PaymentCollectionMethod
            }
        };

        using var response = await _httpClient
            .PostAsJsonAsync("subscriptions.json", request, JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            await ThrowForErrorAsync(response, "create subscription", cancellationToken).ConfigureAwait(false);
        }

        var wrapper = await ReadJsonAsync<MaxioSubscriptionWrapper>(response, "create subscription", cancellationToken)
            .ConfigureAwait(false);

        if (wrapper.Subscription is null)
        {
            throw new SubscriptionBillingException("Maxio returned an empty subscription response.");
        }

        return wrapper.Subscription;
    }

    // --- Mapping & helpers ------------------------------------------------------------------

    private static SubscriptionPlan MapPlan(MaxioProduct product) => new()
    {
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? string.Empty,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        FormattedPrice = FormatPrice(product.PriceInCents),
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? string.Empty,
        RequiresPaymentMethod = product.RequireCreditCard
    };

    private static CustomerSubscription MapSubscription(MaxioSubscription subscription, bool wasCreated) => new()
    {
        Id = subscription.Id,
        State = subscription.State ?? string.Empty,
        PlanHandle = subscription.Product?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? string.Empty,
        PriceInCents = subscription.Product?.PriceInCents ?? 0,
        FormattedPrice = FormatPrice(subscription.Product?.PriceInCents ?? 0),
        IntervalUnit = subscription.Product?.IntervalUnit ?? string.Empty,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingAt = subscription.NextAssessmentAt,
        CreatedAt = subscription.CreatedAt,
        CustomerId = subscription.Customer?.Id ?? 0,
        CustomerReference = subscription.Customer?.Reference,
        WasCreated = wasCreated
    };

    private static string FormatPrice(int cents) =>
        (cents / 100m).ToString("C", PriceCulture);

    private static async Task<T> ReadJsonAsync<T>(
        HttpResponseMessage response,
        string action,
        CancellationToken cancellationToken)
    {
        try
        {
            var value = await response.Content
                .ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);

            if (value is null)
            {
                throw new SubscriptionBillingException($"Maxio returned an empty body when trying to {action}.");
            }

            return value;
        }
        catch (JsonException ex)
        {
            throw new SubscriptionBillingException($"Could not parse the Maxio response when trying to {action}.", ex);
        }
    }

    private async Task ThrowForErrorAsync(
        HttpResponseMessage response,
        string action,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var messages = TryExtractErrors(body);

        _logger.LogWarning(
            $"Maxio: failed to {action} — HTTP {(int)response.StatusCode} {response.ReasonPhrase}. Body: {Truncate(body, 1000)}");

        if (response.StatusCode == HttpStatusCode.UnprocessableEntity && messages.Count > 0)
        {
            throw new SubscriptionValidationException(messages);
        }

        var detail = messages.Count > 0 ? string.Join(" ", messages) : response.ReasonPhrase;
        throw new SubscriptionBillingException(
            $"Maxio billing request failed while trying to {action} (HTTP {(int)response.StatusCode}): {detail}");
    }

    private static List<string> TryExtractErrors(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return new List<string>();
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<MaxioErrorResponse>(body, JsonOptions);
            if (parsed?.Errors is { Count: > 0 })
            {
                return parsed.Errors;
            }
        }
        catch (JsonException)
        {
            // Not the standard error envelope — fall through.
        }

        return new List<string>();
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value.Substring(0, max) + "…";
}
