using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Maxio Advanced Billing is the system of record. Customer and subscription
/// identity is the Maxio <c>reference</c> field (unique per site), so a double-click
/// never creates a second customer or a second subscription for the same plan.
/// </summary>
public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private const string CustomerReferencePrefix = "eshoponweb:";
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserGates = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly HashSet<string> TerminalSubscriptionStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled",
        "cancelled",
        "expired",
        "trial_ended"
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;
    private readonly string _basicAuthValue;

    public MaxioSubscriptionBillingService(
        HttpClient httpClient,
        IOptions<MaxioSettings> options,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _httpClient = httpClient;
        _settings = options.Value;
        _logger = logger;
        _basicAuthValue = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:x"));
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var products = await ListFamilyProductsAsync(cancellationToken);
        return products
            .Where(p => p.ArchivedAt is null && !string.IsNullOrWhiteSpace(p.Handle))
            .Select(ToPlan)
            .ToList();
    }

    public async Task<ShopperSubscription> SubscribeAsync(
        ShopperIdentity shopper,
        string productHandle,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        if (shopper is null) throw new ArgumentNullException(nameof(shopper));
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new BillingException("A productHandle is required to subscribe.", 400);
        }

        productHandle = productHandle.Trim();

        var gate = UserGates.GetOrAdd(shopper.UserId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            await EnsurePlanIsOfferedAsync(productHandle, cancellationToken);

            var customer = await EnsureCustomerAsync(shopper, cancellationToken);
            var subscriptionReference = BuildSubscriptionReference(shopper.UserId, productHandle);

            var existing = await FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
            if (existing?.Subscription is not null && !IsTerminal(existing.Subscription.State))
            {
                _logger.LogInformation(
                    "Returning existing Maxio subscription {SubscriptionId} for user {UserId} and plan {ProductHandle}.",
                    existing.Subscription.Id,
                    shopper.UserId,
                    productHandle);
                return ToShopperSubscription(existing.Subscription);
            }

            var created = await CreateSubscriptionAsync(customer.Id, productHandle, subscriptionReference, cancellationToken);
            return ToShopperSubscription(created);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<ShopperSubscription>> ListShopperSubscriptionsAsync(
        ShopperIdentity shopper,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        if (shopper is null) throw new ArgumentNullException(nameof(shopper));

        var customer = await GetCustomerByReferenceAsync(BuildCustomerReference(shopper.UserId), cancellationToken);
        if (customer is null)
        {
            return Array.Empty<ShopperSubscription>();
        }

        var subscriptions = await ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(ToShopperSubscription).ToList();
    }

    private async Task EnsurePlanIsOfferedAsync(string productHandle, CancellationToken cancellationToken)
    {
        var product = await GetProductByHandleAsync(productHandle, cancellationToken);
        if (product is null || product.ArchivedAt is not null)
        {
            throw new BillingException($"Unknown subscription plan '{productHandle}'.", 400);
        }

        var expectedFamily = _settings.ProductFamilyHandle.Trim();
        var actualFamily = product.ProductFamily?.Handle;
        if (!string.Equals(actualFamily, expectedFamily, StringComparison.OrdinalIgnoreCase))
        {
            throw new BillingException($"Plan '{productHandle}' is not available.", 400);
        }
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(ShopperIdentity shopper, CancellationToken cancellationToken)
    {
        var reference = BuildCustomerReference(shopper.UserId);
        var existing = await GetCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = NamesFromShopper(shopper);
        try
        {
            return await CreateCustomerAsync(firstName, lastName, shopper.Email, reference, cancellationToken);
        }
        catch (BillingException ex) when (ex.StatusCode == 422)
        {
            _logger.LogInformation(
                "Maxio customer create raced for reference {Reference}; looking up the existing customer.",
                reference);
            var raced = await GetCustomerByReferenceAsync(reference, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw;
        }
    }

    private async Task<IReadOnlyList<MaxioProduct>> ListFamilyProductsAsync(CancellationToken cancellationToken)
    {
        var familyId = FamilyPathId(_settings.ProductFamilyHandle);
        using var response = await SendAsync(HttpMethod.Get, $"product_families/{familyId}/products.json?per_page=200", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new BillingException(
                $"Maxio product family '{_settings.ProductFamilyHandle}' was not found.",
                502);
        }

        await EnsureSuccessAsync(response, "list subscription plans");
        var payload = await DeserializeAsync<List<MaxioProductEnvelope>>(response, cancellationToken);
        return payload?.Select(e => e.Product).Where(p => p is not null).Cast<MaxioProduct>().ToList()
               ?? new List<MaxioProduct>();
    }

    private async Task<MaxioProduct?> GetProductByHandleAsync(string handle, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            HttpMethod.Get,
            $"products/handle/{Uri.EscapeDataString(handle)}.json",
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, "read product by handle");
        var envelope = await DeserializeAsync<MaxioProductEnvelope>(response, cancellationToken);
        return envelope?.Product;
    }

    private async Task<MaxioCustomer?> GetCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            HttpMethod.Get,
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}",
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, "lookup customer by reference");
        var envelope = await DeserializeAsync<MaxioCustomerEnvelope>(response, cancellationToken);
        return envelope?.Customer;
    }

    private async Task<MaxioCustomer> CreateCustomerAsync(
        string firstName,
        string lastName,
        string email,
        string reference,
        CancellationToken cancellationToken)
    {
        var body = new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference
            }
        };

        using var response = await SendAsync(HttpMethod.Post, "customers.json", cancellationToken, body);
        if (!response.IsSuccessStatusCode)
        {
            throw await ToBillingExceptionAsync(response, "create customer");
        }

        var envelope = await DeserializeAsync<MaxioCustomerEnvelope>(response, cancellationToken);
        if (envelope?.Customer is null)
        {
            throw new BillingException("Maxio create customer returned an empty payload.");
        }

        _logger.LogInformation("Created Maxio customer {CustomerId} for reference {Reference}.", envelope.Customer.Id, reference);
        return envelope.Customer;
    }

    private async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        long customerId,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, $"customers/{customerId}/subscriptions.json", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return Array.Empty<MaxioSubscription>();
        }

        await EnsureSuccessAsync(response, "list customer subscriptions");
        var payload = await DeserializeAsync<List<MaxioSubscriptionEnvelope>>(response, cancellationToken);
        return payload?.Select(e => e.Subscription).Where(s => s is not null).Cast<MaxioSubscription>().ToList()
               ?? new List<MaxioSubscription>();
    }

    private async Task<MaxioSubscriptionEnvelope?> FindSubscriptionByReferenceAsync(
        string reference,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            HttpMethod.Get,
            $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}",
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, "find subscription by reference");
        return await DeserializeAsync<MaxioSubscriptionEnvelope>(response, cancellationToken);
    }

    private async Task<MaxioSubscription> CreateSubscriptionAsync(
        long customerId,
        string productHandle,
        string reference,
        CancellationToken cancellationToken)
    {
        var body = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscription
            {
                ProductHandle = productHandle,
                CustomerId = customerId,
                Reference = reference,
                PaymentCollectionMethod = "remittance"
            }
        };

        using var response = await SendAsync(HttpMethod.Post, "subscriptions.json", cancellationToken, body);
        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var existing = await FindSubscriptionByReferenceAsync(reference, cancellationToken);
            if (existing?.Subscription is not null)
            {
                return existing.Subscription;
            }
        }

        if (!response.IsSuccessStatusCode)
        {
            throw await ToBillingExceptionAsync(response, "create subscription");
        }

        var envelope = await DeserializeAsync<MaxioSubscriptionEnvelope>(response, cancellationToken);
        if (envelope?.Subscription is null)
        {
            throw new BillingException("Maxio create subscription returned an empty payload.");
        }

        _logger.LogInformation(
            "Created Maxio subscription {SubscriptionId} for customer {CustomerId} on plan {ProductHandle}.",
            envelope.Subscription.Id,
            customerId,
            productHandle);
        return envelope.Subscription;
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string relativeUrl,
        CancellationToken cancellationToken,
        object? body = null)
    {
        using var request = new HttpRequestMessage(method, relativeUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", _basicAuthValue);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        try
        {
            return await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogError(ex, "Maxio Advanced Billing request to {Path} failed.", relativeUrl);
            throw new BillingException("Unable to reach Maxio Advanced Billing.", ex);
        }
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, string operation)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw await ToBillingExceptionAsync(response, operation);
        }
    }

    private async Task<BillingException> ToBillingExceptionAsync(HttpResponseMessage response, string operation)
    {
        var raw = await response.Content.ReadAsStringAsync();
        var detail = TryFormatMaxioError(raw);
        _logger.LogWarning(
            "Maxio {Operation} failed with {StatusCode}: {Detail}",
            operation,
            (int)response.StatusCode,
            detail);

        var status = response.StatusCode == HttpStatusCode.UnprocessableEntity ||
                     response.StatusCode == HttpStatusCode.BadRequest
            ? 400
            : 502;

        var message = string.IsNullOrWhiteSpace(detail)
            ? $"Maxio failed to {operation}."
            : $"Maxio failed to {operation}: {detail}";
        return new BillingException(message, status);
    }

    private static string TryFormatMaxioError(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
            {
                return error.GetString() ?? string.Empty;
            }

            if (!doc.RootElement.TryGetProperty("errors", out var errors))
            {
                return raw.Length > 400 ? raw[..400] : raw;
            }

            return errors.ValueKind switch
            {
                JsonValueKind.String => errors.GetString() ?? string.Empty,
                JsonValueKind.Array => string.Join("; ", errors.EnumerateArray().Select(e => e.ToString())),
                JsonValueKind.Object => string.Join("; ", errors.EnumerateObject().Select(p => $"{p.Name}: {p.Value}")),
                _ => raw.Length > 400 ? raw[..400] : raw
            };
        }
        catch (JsonException)
        {
            return raw.Length > 400 ? raw[..400] : raw;
        }
    }

    private static async Task<T?> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new BillingException("Maxio returned a payload that could not be parsed.", ex);
        }
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            throw new BillingException("Maxio:ApiKey is not configured.", 500);
        }

        if (string.IsNullOrWhiteSpace(_settings.ProductFamilyHandle))
        {
            throw new BillingException("Maxio:ProductFamilyHandle is not configured.", 500);
        }

        if (string.IsNullOrWhiteSpace(_settings.BaseUrl) && string.IsNullOrWhiteSpace(_settings.Subdomain))
        {
            throw new BillingException("Maxio:Subdomain or Maxio:BaseUrl must be configured.", 500);
        }
    }

    internal static string BuildCustomerReference(string userId) => $"{CustomerReferencePrefix}{userId}";

    internal static string BuildSubscriptionReference(string userId, string productHandle)
        => $"{CustomerReferencePrefix}{userId}:{productHandle}";

    internal static string FamilyPathId(string productFamilyHandle)
    {
        var handle = productFamilyHandle.Trim();
        var value = handle.StartsWith("handle:", StringComparison.OrdinalIgnoreCase)
            ? handle
            : $"handle:{handle}";
        return Uri.EscapeDataString(value);
    }

    internal static (string FirstName, string LastName) NamesFromShopper(ShopperIdentity shopper)
    {
        var source = !string.IsNullOrWhiteSpace(shopper.UserName) ? shopper.UserName
            : !string.IsNullOrWhiteSpace(shopper.Email) ? shopper.Email
            : "shopper";

        var local = source.Contains('@') ? source.Split('@')[0] : source;
        var parts = local.Split(new[] { '.', '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            return (Capitalize(parts[0]), Capitalize(parts[1]));
        }

        return (Capitalize(parts.Length == 1 ? parts[0] : "Shopper"), "Customer");
    }

    private static string Capitalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Shopper";
        if (value.Length == 1) return value.ToUpperInvariant();
        return char.ToUpperInvariant(value[0]) + value[1..];
    }

    private static bool IsTerminal(string? state)
        => !string.IsNullOrWhiteSpace(state) && TerminalSubscriptionStates.Contains(state);

    private static SubscriptionPlan ToPlan(MaxioProduct product) => new(
        product.Handle,
        product.Name,
        product.Description ?? string.Empty,
        CentsToAmount(product.PriceInCents),
        product.Interval,
        product.IntervalUnit ?? "month");

    private static ShopperSubscription ToShopperSubscription(MaxioSubscription subscription) => new(
        subscription.Id,
        subscription.Product?.Handle ?? string.Empty,
        subscription.Product?.Name ?? string.Empty,
        CentsToAmount(subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? 0),
        subscription.State,
        subscription.NextAssessmentAt);

    internal static decimal CentsToAmount(long cents) => cents / 100m;
}
