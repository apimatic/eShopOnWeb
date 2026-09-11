using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IMaxioBillingService
{
    Task<MaxioCustomer> EnsureCustomerAsync(string reference, string email, string firstName, string lastName);
    Task<MaxioSubscription> SubscribeAsync(string customerReference, string productHandle);
    Task<List<MaxioSubscription>> ListSubscriptionsAsync(string customerReference);
    Task<List<MaxioPlan>> ListPlansAsync();
}

public record MaxioCustomer(int Id, string Reference, string Email, string FirstName, string LastName);
public record MaxioSubscription(int Id, string State, int ProductId, string ProductHandle, string ProductName, decimal PriceInCents, string? CurrentPeriodEndsAt, string? NextAssessmentAt);
public record MaxioPlan(int Id, string Handle, string Name, decimal PriceInCents, int Interval, string IntervalUnit);
