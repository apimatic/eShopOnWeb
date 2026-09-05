namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record SubscriptionEnrollmentRequest(
    string CustomerReference,
    string Email,
    string FirstName,
    string LastName,
    string PlanHandle,
    int PlanInterval,
    string PlanIntervalUnit);
