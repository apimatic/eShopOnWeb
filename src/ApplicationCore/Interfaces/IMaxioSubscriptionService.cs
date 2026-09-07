using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IMaxioSubscriptionService
{
    Task<MaxioProduct[]> GetProductsAsync();
    Task<MaxioCustomer> GetOrCreateCustomerAsync(string userId, string email);
    Task<MaxioSubscription> CreateSubscriptionAsync(string maxioCustomerId, int maxioProductId);
    Task<MaxioSubscription[]> GetCustomerSubscriptionsAsync(string maxioCustomerId);
}

public class MaxioProduct
{
    public int Id { get; set; }
    public required string Handle { get; set; }
    public required string Name { get; set; }
    public required string Description { get; set; }
    public decimal Price { get; set; }
}

public class MaxioCustomer
{
    public required string Id { get; set; }
    public required string Email { get; set; }
    public required string FirstName { get; set; }
    public string? LastName { get; set; }
}

public class MaxioSubscription
{
    public int Id { get; set; }
    public required string CustomerId { get; set; }
    public int ProductId { get; set; }
    public required string ProductHandle { get; set; }
    public required string State { get; set; }
    public decimal CurrentPrice { get; set; }
    public DateTime? NextBillingDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
