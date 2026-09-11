using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IMaxioApiClient
{
    Task<IReadOnlyList<MaxioProductDto>> ListProductsAsync(string? productFamilyHandle = null);
    Task<MaxioCustomerDto?> FindCustomerByReferenceAsync(string reference);
    Task<MaxioCustomerDto> CreateCustomerAsync(string reference, string email, string firstName, string lastName);
    Task<MaxioSubscriptionDto> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest request);
    Task<IReadOnlyList<MaxioSubscriptionDto>> ListSubscriptionsAsync(string? state = null, int page = 1, int perPage = 20);
    Task<IReadOnlyList<MaxioSubscriptionDto>> ListCustomerSubscriptionsAsync(int customerId, string? state = null);
    Task<MaxioProductDto?> FindProductByHandleAsync(string handle);
}

public class MaxioProductDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public bool RequireCreditCard { get; set; }
    public bool Taxable { get; set; }
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string ProductFamilyName { get; set; } = string.Empty;
}

public class MaxioCustomerDto
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Reference { get; set; }
}

public class MaxioSubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public int PriceInCents { get; set; }
    public int? CustomerId { get; set; }
    public string? CurrentPeriodEndsAt { get; set; }
    public string? NextAssessmentAt { get; set; }
    public string? ActivatedAt { get; set; }
    public string? CanceledAt { get; set; }
    public string? CreatedAt { get; set; }
}

public class MaxioCreateSubscriptionRequest
{
    public string ProductHandle { get; set; } = string.Empty;
    public int CustomerId { get; set; }
}
