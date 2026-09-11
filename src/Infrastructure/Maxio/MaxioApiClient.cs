using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public class MaxioApiClient : IMaxioApiClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioApiClient> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions JsonWriteOpts = new()
    {
        PropertyNamingPolicy = null,
        PropertyNameCaseInsensitive = true
    };

    public MaxioApiClient(
        HttpClient httpClient,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public static void ConfigureHttpClient(HttpClient client, MaxioSettings settings)
    {
        var baseUrl = settings.ResolveBaseUrl();
        client.BaseAddress = new Uri(baseUrl + "/");
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:x"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<IReadOnlyList<MaxioProductDto>> ListProductsAsync(string? productFamilyHandle = null)
    {
        var url = "products.json?per_page=200";
        if (!string.IsNullOrWhiteSpace(productFamilyHandle))
        {
            var families = await GetAsync<JsonElement>("product_families.json?per_page=200");
            var familyArray = families.EnumerateArray();
            foreach (var fam in familyArray)
            {
                var famObj = fam.GetProperty("product_family");
                if (famObj.TryGetProperty("handle", out var h) && h.GetString() == productFamilyHandle)
                {
                    var familyId = famObj.GetProperty("id").GetInt32();
                    url += $"&product_family_id={familyId}";
                    break;
                }
            }
        }

        var products = await GetAsync<List<JsonElement>>(url);
        var result = new List<MaxioProductDto>();
        foreach (var item in products)
        {
            var p = item.GetProperty("product");
            result.Add(new MaxioProductDto
            {
                Id = p.GetProperty("id").GetInt32(),
                Name = p.GetProperty("name").GetString() ?? "",
                Handle = p.TryGetProperty("handle", out var h2) && h2.ValueKind != JsonValueKind.Null ? h2.GetString() ?? "" : "",
                Description = p.TryGetProperty("description", out var d) && d.ValueKind != JsonValueKind.Null ? d.GetString() : null,
                PriceInCents = p.GetProperty("price_in_cents").GetInt32(),
                Interval = p.GetProperty("interval").GetInt32(),
                IntervalUnit = p.GetProperty("interval_unit").GetString() ?? "",
                RequireCreditCard = p.TryGetProperty("require_credit_card", out var rcc) && rcc.GetBoolean(),
                Taxable = p.TryGetProperty("taxable", out var tax) && tax.GetBoolean(),
                ProductFamilyHandle = p.TryGetProperty("product_family", out var pf) && pf.ValueKind != JsonValueKind.Null && pf.TryGetProperty("handle", out var pfh) && pfh.ValueKind != JsonValueKind.Null ? pfh.GetString() ?? "" : "",
                ProductFamilyName = p.TryGetProperty("product_family", out var pf2) && pf2.ValueKind != JsonValueKind.Null && pf2.TryGetProperty("name", out var pfn) && pfn.ValueKind != JsonValueKind.Null ? pfn.GetString() ?? "" : ""
            });
        }
        return result;
    }

    public async Task<MaxioCustomerDto?> FindCustomerByReferenceAsync(string reference)
    {
        try
        {
            var result = await GetAsync<JsonElement>($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}");
            var c = result.GetProperty("customer");
            return MapCustomer(c);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }

    public async Task<MaxioCustomerDto> CreateCustomerAsync(string reference, string email, string firstName, string lastName)
    {
        var body = new
        {
            customer = new
            {
                first_name = firstName,
                last_name = lastName,
                email = email,
                reference = reference
            }
        };

        var result = await PostAsync<JsonElement>("customers.json", body);
        var c = result.GetProperty("customer");
        return MapCustomer(c);
    }

    public async Task<MaxioSubscriptionDto> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest request)
    {
        var body = new
        {
            subscription = new
            {
                product_handle = request.ProductHandle,
                customer_id = request.CustomerId,
                payment_collection_method = "remittance"
            }
        };

        var result = await PostAsync<JsonElement>("subscriptions.json", body);
        var s = result.GetProperty("subscription");
        return MapSubscription(s);
    }

    public async Task<IReadOnlyList<MaxioSubscriptionDto>> ListSubscriptionsAsync(string? state = null, int page = 1, int perPage = 20)
    {
        var url = $"subscriptions.json?page={page}&per_page={perPage}";
        if (!string.IsNullOrWhiteSpace(state))
            url += $"&state={Uri.EscapeDataString(state)}";

        var subscriptions = await GetAsync<List<JsonElement>>(url);
        var result = new List<MaxioSubscriptionDto>();
        foreach (var item in subscriptions)
        {
            var s = item.GetProperty("subscription");
            result.Add(MapSubscription(s));
        }
        return result;
    }

    public async Task<IReadOnlyList<MaxioSubscriptionDto>> ListCustomerSubscriptionsAsync(int customerId, string? state = null)
    {
        var url = $"customers/{customerId}/subscriptions.json?per_page=100";
        if (!string.IsNullOrWhiteSpace(state))
            url += $"&state={Uri.EscapeDataString(state)}";

        var subscriptions = await GetAsync<List<JsonElement>>(url);
        var result = new List<MaxioSubscriptionDto>();
        foreach (var item in subscriptions)
        {
            var s = item.GetProperty("subscription");
            result.Add(MapSubscription(s));
        }
        return result;
    }

    public async Task<MaxioProductDto?> FindProductByHandleAsync(string handle)
    {
        var products = await GetAsync<List<JsonElement>>($"products.json?per_page=200&handle={Uri.EscapeDataString(handle)}");
        foreach (var item in products)
        {
            var p = item.GetProperty("product");
            if (p.TryGetProperty("handle", out var h) && h.GetString() == handle)
            {
                return new MaxioProductDto
                {
                    Id = p.GetProperty("id").GetInt32(),
                    Name = p.GetProperty("name").GetString() ?? "",
                    Handle = handle,
                    Description = p.TryGetProperty("description", out var d) && d.ValueKind != JsonValueKind.Null ? d.GetString() : null,
                    PriceInCents = p.GetProperty("price_in_cents").GetInt32(),
                    Interval = p.GetProperty("interval").GetInt32(),
                    IntervalUnit = p.GetProperty("interval_unit").GetString() ?? "",
                    RequireCreditCard = p.TryGetProperty("require_credit_card", out var rcc) && rcc.GetBoolean(),
                    Taxable = p.TryGetProperty("taxable", out var tax) && tax.GetBoolean(),
                    ProductFamilyHandle = p.TryGetProperty("product_family", out var pf) && pf.ValueKind != JsonValueKind.Null && pf.TryGetProperty("handle", out var pfh) && pfh.ValueKind != JsonValueKind.Null ? pfh.GetString() ?? "" : "",
                    ProductFamilyName = p.TryGetProperty("product_family", out var pf2) && pf2.ValueKind != JsonValueKind.Null && pf2.TryGetProperty("name", out var pfn) && pfn.ValueKind != JsonValueKind.Null ? pfn.GetString() ?? "" : ""
                };
            }
        }
        return null;
    }

    private static MaxioCustomerDto MapCustomer(JsonElement c)
    {
        return new MaxioCustomerDto
        {
            Id = c.GetProperty("id").GetInt32(),
            FirstName = c.GetProperty("first_name").GetString() ?? "",
            LastName = c.GetProperty("last_name").GetString() ?? "",
            Email = c.GetProperty("email").GetString() ?? "",
            Reference = c.TryGetProperty("reference", out var r) && r.ValueKind != JsonValueKind.Null ? r.GetString() : null
        };
    }

    private static MaxioSubscriptionDto MapSubscription(JsonElement s)
    {
        return new MaxioSubscriptionDto
        {
            Id = s.GetProperty("id").GetInt32(),
            State = s.GetProperty("state").GetString() ?? "",
            ProductId = s.TryGetProperty("product", out var prod) && prod.ValueKind != JsonValueKind.Null && prod.TryGetProperty("id", out var pid) ? pid.GetInt32() : 0,
            ProductName = s.TryGetProperty("product", out var prod2) && prod2.ValueKind != JsonValueKind.Null && prod2.TryGetProperty("name", out var pname) && pname.ValueKind != JsonValueKind.Null ? pname.GetString() ?? "" : "",
            ProductHandle = s.TryGetProperty("product", out var prod3) && prod3.ValueKind != JsonValueKind.Null && prod3.TryGetProperty("handle", out var ph) && ph.ValueKind != JsonValueKind.Null ? ph.GetString() ?? "" : "",
            PriceInCents = s.TryGetProperty("product_price_in_cents", out var ppic) ? ppic.GetInt32() : 0,
            CustomerId = s.TryGetProperty("customer", out var cust) && cust.ValueKind != JsonValueKind.Null && cust.TryGetProperty("id", out var cid) ? cid.GetInt32() : null,
            CurrentPeriodEndsAt = s.TryGetProperty("current_period_ends_at", out var cpe) && cpe.ValueKind != JsonValueKind.Null ? cpe.GetString() : null,
            NextAssessmentAt = s.TryGetProperty("next_assessment_at", out var na) && na.ValueKind != JsonValueKind.Null ? na.GetString() : null,
            ActivatedAt = s.TryGetProperty("activated_at", out var act) && act.ValueKind != JsonValueKind.Null ? act.GetString() : null,
            CanceledAt = s.TryGetProperty("canceled_at", out var can) && can.ValueKind != JsonValueKind.Null ? can.GetString() : null,
            CreatedAt = s.TryGetProperty("created_at", out var cr) && cr.ValueKind != JsonValueKind.Null ? cr.GetString() : null
        };
    }

    private async Task<T> GetAsync<T>(string url)
    {
        _logger.LogDebug("Maxio GET {Url}", url);
        var response = await _httpClient.GetAsync(url);
        var content = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Maxio GET {Url} returned {Status}: {Content}", url, response.StatusCode, content);
            response.EnsureSuccessStatusCode();
        }
        return JsonSerializer.Deserialize<T>(content, JsonOpts)!;
    }

    private async Task<T> PostAsync<T>(string url, object body)
    {
        var json = JsonSerializer.Serialize(body, JsonWriteOpts);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(url, content);
        var responseContent = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Maxio POST {Url} returned {Status}: {Content}", url, response.StatusCode, responseContent);
            throw new MaxioApiException((int)response.StatusCode, responseContent);
        }
        return JsonSerializer.Deserialize<T>(responseContent, JsonOpts)!;
    }
}

public class MaxioApiException : Exception
{
    public int StatusCode { get; }
    public string ResponseBody { get; }
    public MaxioApiException(int statusCode, string responseBody)
        : base($"Maxio API returned {statusCode}: {responseBody}")
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }
}
