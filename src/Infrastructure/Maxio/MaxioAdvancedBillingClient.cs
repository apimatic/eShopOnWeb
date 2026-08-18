using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contract;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public class MaxioAdvancedBillingClient : IMaxioAdvancedBillingClient
{
    private const int ProductPageSize = 200;
    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioAdvancedBillingClient> _logger;

    public MaxioAdvancedBillingClient(HttpClient httpClient, ILogger<MaxioAdvancedBillingClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(productFamilyHandle))
        {
            throw new MaxioConfigurationException("Maxio:ProductFamilyHandle is not configured.");
        }

        var familyId = $"handle:{productFamilyHandle.Trim()}";
        var plans = new List<SubscriptionPlan>();
        var page = 1;

        while (true)
        {
            var path = $"/product_families/{Uri.EscapeDataString(familyId)}/products.json?page={page}&per_page={ProductPageSize}";
            var wrappers = await SendAsync<List<ProductResponse>>(HttpMethod.Get, path, null, cancellationToken, allowNotFound: true);
            if (wrappers is null || wrappers.Count == 0)
            {
                break;
            }

            foreach (var wrapper in wrappers)
            {
                var product = wrapper.Product;
                if (product is null || !string.IsNullOrEmpty(product.ArchivedAt) || string.IsNullOrWhiteSpace(product.Handle))
                {
                    continue;
                }

                plans.Add(MapProduct(product));
            }

            if (wrappers.Count < ProductPageSize)
            {
                break;
            }

            page++;
        }

        return plans;
    }

    public async Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var path = $"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var response = await SendAsync<CustomerResponse>(HttpMethod.Get, path, null, cancellationToken, allowNotFound: true);
        return response?.Customer is null ? null : MapCustomer(response.Customer);
    }

    public async Task<BillingCustomer> CreateCustomerAsync(CreateBillingCustomer customer, CancellationToken cancellationToken = default)
    {
        var payload = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = customer.FirstName,
                LastName = customer.LastName,
                Email = customer.Email,
                Reference = customer.Reference
            }
        };

        var response = await SendAsync<CustomerResponse>(HttpMethod.Post, "/customers.json", payload, cancellationToken);
        if (response?.Customer is null)
        {
            throw new MaxioApiException(502, "Maxio create customer returned an empty payload.");
        }

        return MapCustomer(response.Customer);
    }

    public async Task<ShopperSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var path = $"/subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var response = await SendAsync<SubscriptionResponse>(HttpMethod.Get, path, null, cancellationToken, allowNotFound: true);
        return response?.Subscription is null ? null : MapSubscription(response.Subscription);
    }

    public async Task<ShopperSubscription> CreateSubscriptionAsync(CreateBillingSubscription subscription, CancellationToken cancellationToken = default)
    {
        var payload = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = subscription.ProductHandle,
                CustomerId = subscription.CustomerId,
                Reference = subscription.Reference,
                PaymentCollectionMethod = "remittance"
            }
        };

        var response = await SendAsync<SubscriptionResponse>(HttpMethod.Post, "/subscriptions.json", payload, cancellationToken);
        if (response?.Subscription is null)
        {
            throw new MaxioApiException(502, "Maxio create subscription returned an empty payload.");
        }

        return MapSubscription(response.Subscription);
    }

    public async Task<IReadOnlyList<ShopperSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var path = $"/customers/{customerId}/subscriptions.json";
        var wrappers = await SendAsync<List<SubscriptionResponse>>(HttpMethod.Get, path, null, cancellationToken, allowNotFound: true);
        if (wrappers is null)
        {
            return Array.Empty<ShopperSubscription>();
        }

        return wrappers
            .Where(wrapper => wrapper.Subscription is not null)
            .Select(wrapper => MapSubscription(wrapper.Subscription!))
            .ToList();
    }

    private async Task<T?> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken, bool allowNotFound = false)
        where T : class
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, MaxioJson.Options);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound && allowNotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            var errors = ParseErrors(payload);
            var message = errors.Count > 0
                ? string.Join(" ", errors)
                : $"Maxio API {method} {path} failed with status {(int)response.StatusCode}.";
            _logger.LogWarning("Maxio API {Method} {Path} returned {StatusCode}.", method, path, (int)response.StatusCode);
            throw new MaxioApiException((int)response.StatusCode, message, errors);
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        return JsonSerializer.Deserialize<T>(payload, MaxioJson.Options);
    }

    internal static IReadOnlyList<string> ParseErrors(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            if (!document.RootElement.TryGetProperty("errors", out var errorsElement))
            {
                return Array.Empty<string>();
            }

            var messages = new List<string>();
            CollectErrorMessages(errorsElement, messages);
            return messages;
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static void CollectErrorMessages(JsonElement element, List<string> messages)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var text = element.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    messages.Add(text);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectErrorMessages(item, messages);
                }
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    CollectErrorMessages(property.Value, messages);
                }
                break;
        }
    }

    private static BillingCustomer MapCustomer(CustomerResource customer)
    {
        return new BillingCustomer
        {
            Id = customer.Id,
            Reference = customer.Reference,
            Email = customer.Email ?? string.Empty,
            FirstName = customer.FirstName,
            LastName = customer.LastName
        };
    }

    private static SubscriptionPlan MapProduct(ProductResource product)
    {
        return new SubscriptionPlan
        {
            Id = product.Id,
            Handle = product.Handle ?? string.Empty,
            Name = product.Name ?? string.Empty,
            Description = product.Description,
            PriceInCents = product.PriceInCents,
            Price = ToDollars(product.PriceInCents),
            Interval = product.Interval,
            IntervalUnit = product.IntervalUnit ?? string.Empty,
            ProductFamilyHandle = product.ProductFamily?.Handle,
            RequireCreditCard = product.RequireCreditCard
        };
    }

    private static ShopperSubscription MapSubscription(SubscriptionResource subscription)
    {
        var nextBilling = ParseTimestamp(subscription.NextAssessmentAt)
            ?? ParseTimestamp(subscription.CurrentPeriodEndsAt);
        var priceInCents = subscription.ProductPriceInCents != 0
            ? subscription.ProductPriceInCents
            : subscription.Product?.PriceInCents ?? 0;

        return new ShopperSubscription
        {
            Id = subscription.Id,
            State = subscription.State ?? string.Empty,
            ProductHandle = subscription.Product?.Handle,
            ProductName = subscription.Product?.Name,
            PriceInCents = priceInCents,
            Price = ToDollars(priceInCents),
            NextBillingDate = nextBilling,
            CurrentPeriodEndsAt = ParseTimestamp(subscription.CurrentPeriodEndsAt),
            Reference = subscription.Reference,
            CustomerId = subscription.Customer?.Id
        };
    }

    private static decimal ToDollars(long priceInCents) => priceInCents / 100m;

    private static DateTimeOffset? ParseTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            return parsed;
        }

        return null;
    }
}
