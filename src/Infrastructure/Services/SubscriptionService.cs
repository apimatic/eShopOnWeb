using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class SubscriptionService
{
    private readonly MaxioClientService _maxioClient;
    private readonly CatalogContext _context;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(MaxioClientService maxioClient, CatalogContext context, ILogger<SubscriptionService> logger)
    {
        _maxioClient = maxioClient;
        _context = context;
        _logger = logger;
    }

    public async Task<List<ProductDto>> GetProductsAsync()
    {
        var response = await _maxioClient.GetAsync<ProductsResponse>("/products.json");
        if (response == null || response.Products == null)
            return new List<ProductDto>();
        return response.Products;
    }

    public async Task<SubscriptionDto?> CreateSubscriptionAsync(string userId, string planHandle, string email, string firstName, string lastName)
    {
        var createCustomerRequest = new CreateCustomerRequestBody
        {
            Customer = new CustomerDto
            {
                Email = email,
                FirstName = firstName,
                LastName = lastName,
                Reference = userId
            }
        };

        var customerResponse = await _maxioClient.PostAsync<CustomerResponse>("/customers.json", createCustomerRequest);
        if (customerResponse?.Customer == null)
        {
            _logger.LogError("Failed to create Maxio customer for user {UserId}", userId);
            return null;
        }

        var customerId = customerResponse.Customer.Id;

        var createSubscriptionRequest = new CreateSubscriptionRequestBody
        {
            Subscription = new CreateSubscriptionDto
            {
                CustomerId = customerId,
                ProductHandle = planHandle,
                PaymentCollectionMethod = "remittance"
            }
        };

        var subscriptionResponse = await _maxioClient.PostAsync<SubscriptionResponse>("/subscriptions.json", createSubscriptionRequest);
        if (subscriptionResponse?.Subscription == null)
        {
            _logger.LogError("Failed to create subscription for customer {CustomerId}", customerId);
            return null;
        }

        var sub = subscriptionResponse.Subscription;

        var subscription = new Subscription
        {
            UserId = userId,
            MaxioCustomerId = customerId,
            MaxioSubscriptionId = sub.Id,
            PlanHandle = planHandle,
            PlanName = sub.Product?.Name ?? "Unknown",
            PriceInDollars = (sub.ProductPriceInCents ?? 0) / 100m,
            State = sub.State ?? "unknown",
            CurrentPeriodEndsAt = sub.CurrentPeriodEndsAt,
            NextAssessmentAt = sub.NextAssessmentAt,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Subscriptions.Add(subscription);
        await _context.SaveChangesAsync();

        return new SubscriptionDto
        {
            Id = subscription.Id,
            MaxioSubscriptionId = sub.Id,
            PlanHandle = planHandle,
            PlanName = subscription.PlanName,
            PriceInDollars = subscription.PriceInDollars,
            State = subscription.State,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            NextAssessmentAt = subscription.NextAssessmentAt,
            CreatedAt = subscription.CreatedAt
        };
    }

    public async Task<List<SubscriptionDto>> GetUserSubscriptionsAsync(string userId)
    {
        var subscriptions = await _context.Subscriptions
            .Where(s => s.UserId == userId)
            .ToListAsync();

        return subscriptions.Select(s => new SubscriptionDto
        {
            Id = s.Id,
            MaxioSubscriptionId = s.MaxioSubscriptionId,
            PlanHandle = s.PlanHandle,
            PlanName = s.PlanName,
            PriceInDollars = s.PriceInDollars,
            State = s.State,
            CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
            NextAssessmentAt = s.NextAssessmentAt,
            CreatedAt = s.CreatedAt
        }).ToList();
    }

    public class ProductsResponse
    {
        [JsonPropertyName("products")]
        public List<ProductDto>? Products { get; set; }
    }

    public class ProductDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("handle")]
        public string? Handle { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("price_in_cents")]
        public int PriceInCents { get; set; }

        [JsonPropertyName("interval")]
        public int Interval { get; set; }

        [JsonPropertyName("interval_unit")]
        public string? IntervalUnit { get; set; }

        [JsonPropertyName("accounting_code")]
        public string? AccountingCode { get; set; }
    }

    public class SubscriptionDto
    {
        public int Id { get; set; }
        public int MaxioSubscriptionId { get; set; }
        public string PlanHandle { get; set; } = string.Empty;
        public string PlanName { get; set; } = string.Empty;
        public decimal PriceInDollars { get; set; }
        public string State { get; set; } = string.Empty;
        public DateTime? CurrentPeriodEndsAt { get; set; }
        public DateTime? NextAssessmentAt { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class CustomerDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("first_name")]
        public string? FirstName { get; set; }

        [JsonPropertyName("last_name")]
        public string? LastName { get; set; }

        [JsonPropertyName("reference")]
        public string? Reference { get; set; }
    }

    public class CreateCustomerRequestBody
    {
        [JsonPropertyName("customer")]
        public CustomerDto? Customer { get; set; }
    }

    public class CustomerResponse
    {
        [JsonPropertyName("customer")]
        public CustomerDto? Customer { get; set; }
    }

    public class CreateSubscriptionDto
    {
        [JsonPropertyName("customer_id")]
        public int CustomerId { get; set; }

        [JsonPropertyName("product_handle")]
        public string? ProductHandle { get; set; }

        [JsonPropertyName("payment_collection_method")]
        public string? PaymentCollectionMethod { get; set; }
    }

    public class SubscriptionInnerDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("state")]
        public string? State { get; set; }

        [JsonPropertyName("product_price_in_cents")]
        public int? ProductPriceInCents { get; set; }

        [JsonPropertyName("current_period_ends_at")]
        public DateTime? CurrentPeriodEndsAt { get; set; }

        [JsonPropertyName("next_assessment_at")]
        public DateTime? NextAssessmentAt { get; set; }

        [JsonPropertyName("product")]
        public ProductDto? Product { get; set; }
    }

    public class CreateSubscriptionRequestBody
    {
        [JsonPropertyName("subscription")]
        public CreateSubscriptionDto? Subscription { get; set; }
    }

    public class SubscriptionResponse
    {
        [JsonPropertyName("subscription")]
        public SubscriptionInnerDto? Subscription { get; set; }
    }
}
