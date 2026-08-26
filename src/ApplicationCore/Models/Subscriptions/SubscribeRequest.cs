namespace Microsoft.eShopWeb.ApplicationCore.Models.Subscriptions;

/// <summary>
/// Instruction to enroll a customer in a subscription plan.
/// </summary>
public class SubscribeRequest
{
    /// <summary>
    /// Unique, stable identifier of the customer in eShopOnWeb. Stored on the billing
    /// customer as its reference so the customer is created at most once.
    /// </summary>
    public string CustomerReference { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    /// <summary>
    /// API handle of the plan (product) to subscribe to.
    /// </summary>
    public string PlanHandle { get; set; } = string.Empty;
}
