using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.ApplicationCore;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    public MaxioSubscriptionService(
        HttpClient httpClient,
        IOptions<MaxioOptions> options,
        ILogger<MaxioSubscriptionService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MaxioPlan>> GetPlansAsync(string productFamilyHandle)
    {
        var families = await GetFromApiAsync<JsonElement>("/product_families.json");
        int? familyId = null;

        foreach (var familyElement in families.EnumerateArray())
        {
            var family = familyElement.GetProperty("product_family");
            if (family.TryGetProperty("handle", out var handleProp) &&
                handleProp.GetString() == productFamilyHandle)
            {
                familyId = family.GetProperty("id").GetInt32();
                break;
            }
        }

        if (familyId == null)
        {
            _logger.LogWarning("Product family with handle '{Handle}' not found", productFamilyHandle);
            return Array.Empty<MaxioPlan>();
        }

        var products = await GetFromApiAsync<JsonElement>($"/product_families/{familyId.Value}/products.json");
        var plans = new List<MaxioPlan>();

        foreach (var productElement in products.EnumerateArray())
        {
            var product = productElement.GetProperty("product");
            plans.Add(new MaxioPlan
            {
                Id = product.GetProperty("id").GetInt32(),
                Name = product.GetProperty("name").GetString() ?? string.Empty,
                Handle = product.GetProperty("handle").GetString() ?? string.Empty,
                Description = product.TryGetProperty("description", out var desc) ? desc.GetString() : null,
                PriceInCents = product.GetProperty("price_in_cents").GetInt32(),
                Interval = product.TryGetProperty("interval", out var interval) ? interval.GetInt32() : 1,
                IntervalUnit = product.TryGetProperty("interval_unit", out var unit) ? unit.GetString() ?? "month" : "month"
            });
        }

        return plans;
    }

    public async Task<MaxioSubscription> SubscribeAsync(string userReference, string productHandle)
    {
        var customer = await FindOrCreateCustomerAsync(userReference);

        var existingSubscription = await FindExistingSubscriptionAsync(customer.Id, productHandle);
        if (existingSubscription != null)
        {
            _logger.LogInformation("Returning existing subscription {Id} for customer {CustomerId}",
                existingSubscription.Id, customer.Id);
            return existingSubscription;
        }

        _logger.LogInformation("Creating subscription for customer {CustomerId}, product {ProductHandle}",
            customer.Id, productHandle);

        var createBody = new
        {
            subscription = new
            {
                customer_id = customer.Id,
                product_handle = productHandle,
                payment_collection_method = "remittance"
            }
        };

        var response = await PostToApiAsync("/subscriptions.json", createBody);
        var subscriptionElement = response.GetProperty("subscription");
        return ParseSubscription(subscriptionElement);
    }

    public async Task<IReadOnlyList<MaxioSubscription>> GetMySubscriptionsAsync(string userReference)
    {
        var allSubscriptions = new List<MaxioSubscription>();
        var seenIds = new HashSet<int>();

        var customer = await FindCustomerByReferenceAsync(userReference);
        if (customer != null)
        {
            await AddSubscriptionsForCustomer(customer.Id, allSubscriptions, seenIds);
        }

        string email = userReference.Contains('@') ? userReference : $"{userReference}@eshop.local";
        try
        {
            var customers = await GetFromApiAsync<JsonElement>($"/customers.json?q={Uri.EscapeDataString(email)}");
            foreach (var customerElement in customers.EnumerateArray())
            {
                var cust = customerElement.GetProperty("customer");
                var custEmail = cust.TryGetProperty("email", out var e) ? e.GetString() : null;
                if (string.Equals(custEmail, email, StringComparison.OrdinalIgnoreCase))
                {
                    var custId = cust.GetProperty("id").GetInt32();
                    if (customer == null || custId != customer.Id)
                    {
                        await AddSubscriptionsForCustomer(custId, allSubscriptions, seenIds);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error searching for additional customers by email '{Email}'", email);
        }

        return allSubscriptions;
    }

    private async Task AddSubscriptionsForCustomer(int customerId, List<MaxioSubscription> result, HashSet<int> seenIds)
    {
        try
        {
            var subscriptions = await GetFromApiAsync<JsonElement>($"/customers/{customerId}/subscriptions.json");
            foreach (var subElement in subscriptions.EnumerateArray())
            {
                var sub = subElement.GetProperty("subscription");
                var subId = sub.GetProperty("id").GetInt32();
                if (seenIds.Add(subId))
                {
                    result.Add(ParseSubscription(sub));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error fetching subscriptions for customer {CustomerId}", customerId);
        }
    }

    private async Task<MaxioCustomer> FindOrCreateCustomerAsync(string userReference)
    {
        var existing = await FindCustomerByReferenceAsync(userReference);
        if (existing != null)
        {
            return existing;
        }

        _logger.LogInformation("Creating Maxio customer for reference '{Reference}'", userReference);

        string email = userReference.Contains('@') ? userReference : $"{userReference}@eshop.local";
        string firstName = "eShop";
        string lastName = "Subscriber";

        var createBody = new
        {
            customer = new
            {
                first_name = firstName,
                last_name = lastName,
                email = email,
                reference = userReference
            }
        };

        var response = await PostToApiAsync("/customers.json", createBody);
        var customerElement = response.GetProperty("customer");

        return new MaxioCustomer
        {
            Id = customerElement.GetProperty("id").GetInt32(),
            Email = email,
            FirstName = firstName,
            LastName = lastName,
            Reference = userReference
        };
    }

    private async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string userReference)
    {
        try
        {
            var response = await GetFromApiAsync<JsonElement>($"/customers/lookup.json?reference={Uri.EscapeDataString(userReference)}");
            var customerElement = response.GetProperty("customer");
            return new MaxioCustomer
            {
                Id = customerElement.GetProperty("id").GetInt32(),
                Email = customerElement.TryGetProperty("email", out var email) ? email.GetString() ?? string.Empty : string.Empty,
                FirstName = customerElement.TryGetProperty("first_name", out var fn) ? fn.GetString() ?? string.Empty : string.Empty,
                LastName = customerElement.TryGetProperty("last_name", out var ln) ? ln.GetString() ?? string.Empty : string.Empty,
                Reference = userReference
            };
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogInformation("Customer not found by reference '{Reference}', trying email search", userReference);
            return await FindCustomerByEmailAsync(userReference);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error looking up customer by reference '{Reference}'", userReference);
            return await FindCustomerByEmailAsync(userReference);
        }
    }

    private async Task<MaxioCustomer?> FindCustomerByEmailAsync(string emailOrReference)
    {
        string email = emailOrReference.Contains('@') ? emailOrReference : $"{emailOrReference}@eshop.local";
        try
        {
            var customers = await GetFromApiAsync<JsonElement>($"/customers.json?q={Uri.EscapeDataString(email)}");

            foreach (var customerElement in customers.EnumerateArray())
            {
                var customer = customerElement.GetProperty("customer");
                var customerEmail = customer.TryGetProperty("email", out var e) ? e.GetString() : null;
                if (string.Equals(customerEmail, email, StringComparison.OrdinalIgnoreCase))
                {
                    var reference = customer.TryGetProperty("reference", out var r) ? r.GetString() : null;
                    return new MaxioCustomer
                    {
                        Id = customer.GetProperty("id").GetInt32(),
                        Email = email,
                        FirstName = customer.TryGetProperty("first_name", out var fn) ? fn.GetString() ?? string.Empty : string.Empty,
                        LastName = customer.TryGetProperty("last_name", out var ln) ? ln.GetString() ?? string.Empty : string.Empty,
                        Reference = reference ?? email
                    };
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error searching customers by email '{Email}'", email);
        }

        return null;
    }

    private async Task<MaxioSubscription?> FindExistingSubscriptionAsync(int customerId, string productHandle)
    {
        try
        {
            var subscriptions = await GetFromApiAsync<JsonElement>($"/customers/{customerId}/subscriptions.json");

            foreach (var subElement in subscriptions.EnumerateArray())
            {
                var sub = subElement.GetProperty("subscription");
                var state = sub.TryGetProperty("state", out var s) ? s.GetString() : null;

                if (state == "canceled" || state == "expired" || state == "suspended")
                    continue;

                string? handle = null;
                if (sub.TryGetProperty("product", out var product) && product.ValueKind == JsonValueKind.Object)
                {
                    handle = product.TryGetProperty("handle", out var h) ? h.GetString() : null;
                }

                if (handle == productHandle)
                {
                    return ParseSubscription(sub);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking existing subscriptions for customer {CustomerId}", customerId);
        }

        return null;
    }

    private static MaxioSubscription ParseSubscription(JsonElement sub)
    {
        string productHandle = string.Empty;
        string productName = string.Empty;
        int productId = 0;

        if (sub.TryGetProperty("product", out var product) && product.ValueKind == JsonValueKind.Object)
        {
            productHandle = product.TryGetProperty("handle", out var h) ? h.GetString() ?? string.Empty : string.Empty;
            productName = product.TryGetProperty("name", out var pn) ? pn.GetString() ?? string.Empty : string.Empty;
            productId = product.TryGetProperty("id", out var pid) ? pid.GetInt32() : 0;
        }

        int amountInCents = sub.TryGetProperty("product_price_in_cents", out var ppc) ? ppc.GetInt32() : 0;

        return new MaxioSubscription
        {
            Id = sub.GetProperty("id").GetInt32(),
            State = sub.TryGetProperty("state", out var s) ? s.GetString() ?? "unknown" : "unknown",
            ProductHandle = productHandle,
            ProductName = productName,
            AmountInCents = amountInCents,
            CurrentPeriodEndsAt = sub.TryGetProperty("current_period_ends_at", out var cpe) ? cpe.GetString() : null,
            NextBillingAt = sub.TryGetProperty("next_assessment_at", out var nba) ? nba.GetString() : null,
            CustomerId = sub.TryGetProperty("customer_id", out var cid) ? cid.GetInt32() : 0,
            ProductId = productId
        };
    }

    private async Task<T> GetFromApiAsync<T>(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{_options.BaseUrl}{path}");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var response = await _httpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Maxio API GET {Path} returned {StatusCode}: {Content}",
                path, response.StatusCode, content);
            throw new HttpRequestException(
                $"Maxio API returned {(int)response.StatusCode}", null, response.StatusCode);
        }

        return JsonSerializer.Deserialize<T>(content, JsonOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize Maxio response for {path}");
    }

    private async Task<JsonElement> PostToApiAsync(string path, object body)
    {
        var json = JsonSerializer.Serialize(body, JsonOptions);
        var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.BaseUrl}{path}")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var response = await _httpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Maxio API POST {Path} returned {StatusCode}: {Content}",
                path, response.StatusCode, content);
            throw new HttpRequestException(
                $"Maxio API returned {(int)response.StatusCode}", null, response.StatusCode);
        }

        return JsonSerializer.Deserialize<JsonElement>(content, JsonOptions);
    }
}
