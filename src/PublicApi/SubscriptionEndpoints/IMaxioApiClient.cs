using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public interface IMaxioApiClient
{
    Task<MaxioProductDto[]> ListProductsAsync(string familyHandle);
    Task<MaxioCustomerDto> GetOrCreateCustomerAsync(string customerId, string email, string firstName, string lastName);
    Task<MaxioCustomerDto> GetCustomerByReferenceAsync(string reference);
    Task<MaxioSubscriptionDto> CreateSubscriptionAsync(CreateMaxioSubscriptionRequest request);
    Task<MaxioSubscriptionDto[]> ListSubscriptionsAsync(string customerId);
}

public class CreateMaxioSubscriptionRequest
{
    public int? CustomerId { get; set; }
    public string? ProductHandle { get; set; }
    public int? ProductId { get; set; }
    public string? PaymentCollectionMethod { get; set; } = "remittance";
}

public class MaxioProductDto
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public int PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public bool RequiresCreditCard { get; set; }
}

public class MaxioCustomerDto
{
    public int Id { get; set; }
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Reference { get; set; }
}

public class MaxioSubscriptionDto
{
    public int Id { get; set; }
    public string? State { get; set; }
    public MaxioSubscriptionProductDto? Product { get; set; }
    public MaxioSubscriptionCustomerDto? Customer { get; set; }
    public string? CurrentPeriodEndsAt { get; set; }
    public string? NextAssessmentAt { get; set; }
    public string? ActivatedAt { get; set; }
    public string? CreatedAt { get; set; }
}

public class MaxioSubscriptionProductDto
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public int PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
}

public class MaxioSubscriptionCustomerDto
{
    public int Id { get; set; }
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}
