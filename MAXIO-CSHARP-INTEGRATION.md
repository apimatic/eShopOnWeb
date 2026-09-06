# Maxio Advanced Billing API - C# Integration Guide

## Configuration

### appsettings.json Structure

```json
{
  "Maxio": {
    "ApiKey": "${MAXIO_API_KEY}",
    "Subdomain": "${MAXIO_SITE_SUBDOMAIN}",
    "ProductFamilyHandle": "${MAXIO_DEFAULT_PRODUCT_FAMILY}",
    "BaseUrl": ""
  }
}
```

### Configuration Binding (C#)

```csharp
public class MaxioSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }

    public string GetBaseUrl()
    {
        if (!string.IsNullOrEmpty(BaseUrl))
            return BaseUrl.TrimEnd('/');
        
        return $"https://{Subdomain}.chargify.com/api/v2";
    }

    public string GetAuthorizationHeader()
    {
        string credentials = $"{ApiKey}:x";
        byte[] credentialsBytes = Encoding.UTF8.GetBytes(credentials);
        string base64Credentials = Convert.ToBase64String(credentialsBytes);
        return $"Basic {base64Credentials}";
    }
}
```

### Dependency Injection Setup

```csharp
// In Program.cs or DependencyContainer
builder.Services.Configure<MaxioSettings>(
    builder.Configuration.GetSection("Maxio"));

builder.Services.AddHttpClient<IMaxioBillingService, MaxioBillingService>()
    .ConfigureHttpClient((provider, client) =>
    {
        var settings = provider.GetRequiredService<IOptions<MaxioSettings>>().Value;
        client.BaseAddress = new Uri(settings.GetBaseUrl());
        client.DefaultRequestHeaders.Authorization = 
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Basic", 
                Convert.ToBase64String(
                    Encoding.UTF8.GetBytes($"{settings.ApiKey}:x")));
        client.DefaultRequestHeaders.Add("Accept", "application/json");
    });
```

---

## Request/Response Models

### Customer Models

```csharp
// Request
public class CreateCustomerRequest
{
    [JsonPropertyName("customer")]
    public CustomerData Customer { get; set; } = new();
}

public class CustomerData
{
    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("phone")]
    public string? Phone { get; set; }

    [JsonPropertyName("organization_name")]
    public string? OrganizationName { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("address")]
    public string? Address { get; set; }

    [JsonPropertyName("address_2")]
    public string? Address2 { get; set; }

    [JsonPropertyName("city")]
    public string? City { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("zip")]
    public string? Zip { get; set; }

    [JsonPropertyName("country_code")]
    public string? CountryCode { get; set; }

    [JsonPropertyName("vat_number")]
    public string? VatNumber { get; set; }
}

// Response
public class CreateCustomerResponse
{
    [JsonPropertyName("customer")]
    public Customer Customer { get; set; } = new();
}

public class Customer
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("phone")]
    public string? Phone { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; }

    [JsonPropertyName("verified")]
    public bool Verified { get; set; }
}
```

### Product/Plan Models

```csharp
public class ListProductsResponse
{
    [JsonPropertyName("products")]
    public List<Product> Products { get; set; } = new();
}

public class Product
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("handle")]
    public string Handle { get; set; } = string.Empty;

    [JsonPropertyName("price_in_cents")]
    public long PriceInCents { get; set; }

    [JsonPropertyName("interval")]
    public int Interval { get; set; }

    [JsonPropertyName("interval_unit")]
    public string IntervalUnit { get; set; } = "month";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("require_payment_method")]
    public bool RequirePaymentMethod { get; set; }

    [JsonPropertyName("taxable")]
    public bool Taxable { get; set; }

    public decimal GetPriceInDollars() => PriceInCents / 100m;
}
```

### Subscription Models

```csharp
// Request
public class CreateSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public SubscriptionData Subscription { get; set; } = new();
}

public class SubscriptionData
{
    [JsonPropertyName("customer_id")]
    public long? CustomerId { get; set; }

    [JsonPropertyName("product_id")]
    public long? ProductId { get; set; }

    [JsonPropertyName("product_handle")]
    public string? ProductHandle { get; set; }

    [JsonPropertyName("payment_collection_method")]
    public string PaymentCollectionMethod { get; set; } = "automatic";

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("coupon_codes")]
    public List<string>? CouponCodes { get; set; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, string>? Metadata { get; set; }
}

// Response
public class CreateSubscriptionResponse
{
    [JsonPropertyName("subscription")]
    public Subscription Subscription { get; set; } = new();
}

public class GetSubscriptionResponse
{
    [JsonPropertyName("subscription")]
    public Subscription Subscription { get; set; } = new();
}

public class Subscription
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("customer_id")]
    public long CustomerId { get; set; }

    [JsonPropertyName("product_id")]
    public long ProductId { get; set; }

    [JsonPropertyName("product_handle")]
    public string ProductHandle { get; set; } = string.Empty;

    [JsonPropertyName("product_name")]
    public string ProductName { get; set; } = string.Empty;

    [JsonPropertyName("next_assessment_at")]
    public DateTime? NextAssessmentAt { get; set; }

    [JsonPropertyName("current_period_starts_at")]
    public DateTime? CurrentPeriodStartsAt { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTime? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("balance_in_cents")]
    public long BalanceInCents { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("activated_at")]
    public DateTime? ActivatedAt { get; set; }

    [JsonPropertyName("canceled_at")]
    public DateTime? CanceledAt { get; set; }

    public decimal GetBalanceInDollars() => BalanceInCents / 100m;
}
```

---

## Service Interface and Implementation

```csharp
public interface IMaxioBillingService
{
    Task<Customer> GetOrCreateCustomerAsync(
        string firstName, 
        string lastName, 
        string email,
        CancellationToken ct = default);
    
    Task<List<Product>> GetProductsAsync(
        CancellationToken ct = default);
    
    Task<Subscription> CreateSubscriptionAsync(
        long customerId,
        long productId,
        CancellationToken ct = default);
    
    Task<Subscription> GetSubscriptionAsync(
        long subscriptionId,
        CancellationToken ct = default);
}

public class MaxioBillingService : IMaxioBillingService
{
    private readonly HttpClient _httpClient;
    private readonly IOptions<MaxioSettings> _settings;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(
        HttpClient httpClient,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioBillingService> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;
    }

    public async Task<Customer> GetOrCreateCustomerAsync(
        string firstName,
        string lastName,
        string email,
        CancellationToken ct = default)
    {
        try
        {
            // Try to find existing customer
            var existingCustomer = await SearchCustomerByEmailAsync(email, ct);
            if (existingCustomer != null)
            {
                _logger.LogInformation("Found existing customer {CustomerId}", existingCustomer.Id);
                return existingCustomer;
            }

            // Create new customer
            var request = new CreateCustomerRequest
            {
                Customer = new CustomerData
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = email,
                    Reference = email // Use email as reference for idempotency
                }
            };

            var response = await _httpClient.PostAsJsonAsync(
                "/customers",
                request,
                cancellationToken: ct);

            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsAsync<CreateCustomerResponse>(
                cancellationToken: ct);

            _logger.LogInformation(
                "Created customer {CustomerId} for {Email}",
                content.Customer.Id,
                email);

            return content.Customer;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Error getting or creating customer for {Email}", email);
            throw;
        }
    }

    public async Task<List<Product>> GetProductsAsync(
        CancellationToken ct = default)
    {
        try
        {
            var productFamilyId = _settings.Value.ProductFamilyHandle;
            
            // First get product family to resolve ID if we only have handle
            var response = await _httpClient.GetAsync(
                $"/products?include=product_family",
                cancellationToken: ct);

            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsAsync<ListProductsResponse>(
                cancellationToken: ct);

            _logger.LogInformation(
                "Retrieved {ProductCount} products from family",
                content.Products.Count);

            return content.Products;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Error retrieving products");
            throw;
        }
    }

    public async Task<Subscription> CreateSubscriptionAsync(
        long customerId,
        long productId,
        CancellationToken ct = default)
    {
        try
        {
            var request = new CreateSubscriptionRequest
            {
                Subscription = new SubscriptionData
                {
                    CustomerId = customerId,
                    ProductId = productId,
                    PaymentCollectionMethod = "automatic"
                }
            };

            var response = await _httpClient.PostAsJsonAsync(
                "/subscriptions",
                request,
                cancellationToken: ct);

            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsAsync<CreateSubscriptionResponse>(
                cancellationToken: ct);

            _logger.LogInformation(
                "Created subscription {SubscriptionId} for customer {CustomerId}",
                content.Subscription.Id,
                customerId);

            return content.Subscription;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(
                ex,
                "Error creating subscription for customer {CustomerId}, product {ProductId}",
                customerId,
                productId);
            throw;
        }
    }

    public async Task<Subscription> GetSubscriptionAsync(
        long subscriptionId,
        CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"/subscriptions/{subscriptionId}?include=customer,product",
                cancellationToken: ct);

            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsAsync<GetSubscriptionResponse>(
                cancellationToken: ct);

            _logger.LogInformation(
                "Retrieved subscription {SubscriptionId}, state: {State}",
                subscriptionId,
                content.Subscription.State);

            return content.Subscription;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Error retrieving subscription {SubscriptionId}", subscriptionId);
            throw;
        }
    }

    private async Task<Customer?> SearchCustomerByEmailAsync(
        string email,
        CancellationToken ct)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"/customers?search={Uri.EscapeDataString(email)}",
                cancellationToken: ct);

            if (!response.IsSuccessStatusCode)
                return null;

            var content = await response.Content.ReadAsAsync<CustomerSearchResponse>(
                cancellationToken: ct);

            return content.Customers?.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error searching for customer by email");
            return null;
        }
    }
}

public class CustomerSearchResponse
{
    [JsonPropertyName("customers")]
    public List<Customer>? Customers { get; set; }
}
```

---

## Usage Example (in API Endpoint)

```csharp
[ApiEndpoint("/api/subscriptions", "POST")]
public class CreateSubscriptionEndpoint : IEndpoint
{
    private readonly IMaxioBillingService _billingService;
    private readonly ILogger<CreateSubscriptionEndpoint> _logger;

    public CreateSubscriptionEndpoint(
        IMaxioBillingService billingService,
        ILogger<CreateSubscriptionEndpoint> logger)
    {
        _billingService = billingService;
        _logger = logger;
    }

    public async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        ITokenClaimsService claimsService,
        CancellationToken ct)
    {
        try
        {
            // Get current user identity
            var userId = claimsService.GetSubjectIdFromToken();
            var userEmail = claimsService.GetEmailFromToken();

            if (string.IsNullOrEmpty(userEmail))
                return Results.Unauthorized();

            // Ensure customer exists in Maxio
            var customer = await _billingService.GetOrCreateCustomerAsync(
                request.FirstName,
                request.LastName,
                userEmail,
                ct);

            // Create subscription
            var subscription = await _billingService.CreateSubscriptionAsync(
                customer.Id,
                request.ProductId,
                ct);

            // Retrieve full subscription details
            var subscriptionDetails = await _billingService.GetSubscriptionAsync(
                subscription.Id,
                ct);

            return Results.Created(
                $"/api/subscriptions/{subscriptionDetails.Id}",
                new
                {
                    subscription = new
                    {
                        subscriptionDetails.Id,
                        subscriptionDetails.State,
                        ProductName = subscriptionDetails.ProductName,
                        NextBillingDate = subscriptionDetails.NextAssessmentAt,
                        CreatedAt = subscriptionDetails.CreatedAt
                    }
                });
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Maxio API error creating subscription");
            return Results.BadRequest(new { error = "Failed to create subscription" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error creating subscription");
            return Results.StatusCode(500);
        }
    }
}

public class CreateSubscriptionRequest
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public long ProductId { get; set; }
}
```

---

## Error Handling Pattern

```csharp
public class MaxioBillingException : Exception
{
    public int? HttpStatusCode { get; set; }
    public string? ResponseContent { get; set; }

    public MaxioBillingException(string message) : base(message) { }

    public MaxioBillingException(
        string message,
        int statusCode,
        string responseContent,
        Exception? innerException = null)
        : base(message, innerException)
    {
        HttpStatusCode = statusCode;
        ResponseContent = responseContent;
    }
}

// Usage in service
private async Task<T> SendRequestAsync<T>(
    HttpRequestMessage request,
    CancellationToken ct)
{
    var response = await _httpClient.SendAsync(request, ct);
    var content = await response.Content.ReadAsStringAsync(ct);

    if (!response.IsSuccessStatusCode)
    {
        throw new MaxioBillingException(
            $"Maxio API error: {response.StatusCode}",
            (int)response.StatusCode,
            content);
    }

    return JsonSerializer.Deserialize<T>(content) 
        ?? throw new InvalidOperationException("Null response");
}
```

---

## Testing with HttpClient Mocking

```csharp
[Test]
public async Task CreateCustomer_WithValidEmail_ReturnsCustomerId()
{
    // Arrange
    var mockHttpClientFactory = new Mock<IHttpClientFactory>();
    var mockResponse = new HttpResponseMessage(System.Net.HttpStatusCode.Created)
    {
        Content = new StringContent("""
        {
          "customer": {
            "id": 12345678,
            "first_name": "John",
            "last_name": "Doe",
            "email": "john@example.com",
            "created_at": "2026-09-06T10:15:30Z"
          }
        }
        """)
    };

    var mockHandler = new Mock<HttpMessageHandler>();
    mockHandler
        .Protected()
        .Setup<Task<HttpResponseMessage>>(
            "SendAsync",
            ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.PathAndQuery.Contains("/customers")),
            ItExpr.IsAny<CancellationToken>())
        .ReturnsAsync(mockResponse);

    var client = new HttpClient(mockHandler.Object)
    {
        BaseAddress = new Uri("https://cp-exp-4.chargify.com/api/v2")
    };

    var service = new MaxioBillingService(client, Options.Create(new MaxioSettings()), new Mock<ILogger<MaxioBillingService>>().Object);

    // Act
    var result = await service.GetOrCreateCustomerAsync("John", "Doe", "john@example.com");

    // Assert
    Assert.That(result.Id, Is.EqualTo(12345678));
    Assert.That(result.Email, Is.EqualTo("john@example.com"));
}
```

---

## Implementation Notes

1. **Always use async/await** - Maxio API calls are I/O bound
2. **Implement logging** - All HTTP calls should be logged with request/response details
3. **Use CancellationToken** - Allow cancellation of long-running requests
4. **Cache product list** - Don't fetch products on every subscription create
5. **Idempotency** - Always check for existing customer before creating new one
6. **Error handling** - Catch specific HTTP errors (401=auth, 404=not found, 422=validation)
7. **Timeouts** - Set reasonable HttpClient timeouts
8. **Retry logic** - Consider implementing exponential backoff for transient failures
9. **Test in sandbox** - All integration testing uses cp-exp-4 site

---

## Configuration Example (User Secrets)

```bash
# Run from src/PublicApi directory
dotnet user-secrets init
dotnet user-secrets set "Maxio:ApiKey" "your-sandbox-api-key"
dotnet user-secrets set "Maxio:Subdomain" "cp-exp-4"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"
dotnet user-secrets set "Maxio:BaseUrl" ""
```

Never commit actual API keys - always use user-secrets or environment variables.

