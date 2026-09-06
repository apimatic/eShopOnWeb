using System;
using System.Threading.Tasks;
using System.Threading;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IMaxioService
{
    Task<MaxioSubscriptionPlan[]> GetSubscriptionPlansAsync(CancellationToken cancellationToken = default);
    Task<MaxioCustomerResponse> GetOrCreateCustomerAsync(string userReference, string firstName, string lastName, string email, CancellationToken cancellationToken = default);
    Task<MaxioSubscriptionResponse> CreateSubscriptionAsync(int customerId, string productHandle, CancellationToken cancellationToken = default);
    Task<MaxioSubscription[]> GetCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);
}

public class MaxioSubscriptionPlan
{
    public int Id { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
    public int PriceInCents { get; set; }
    public string? Description { get; set; }
}

public class MaxioCustomerResponse
{
    public int Id { get; set; }
    public string? Reference { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
}

public class MaxioSubscriptionResponse
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public int ProductId { get; set; }
    public string? State { get; set; }
    public DateTime? NextBillingAt { get; set; }
}

public class MaxioSubscription
{
    public int Id { get; set; }
    public string? State { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? NextBillingAt { get; set; }
    public string? ProductHandle { get; set; }
}
