using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A request to enroll an authenticated eShopOnWeb user onto a plan.
/// </summary>
public class SubscribeRequest
{
    public SubscribeRequest(
        string userName,
        string? planHandle = null,
        string? idempotencyKey = null,
        string? firstName = null,
        string? lastName = null)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new ArgumentException("A user name is required.", nameof(userName));
        }

        UserName = userName.Trim();
        PlanHandle = string.IsNullOrWhiteSpace(planHandle) ? null : planHandle.Trim();
        IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim();
        FirstName = string.IsNullOrWhiteSpace(firstName) ? null : firstName.Trim();
        LastName = string.IsNullOrWhiteSpace(lastName) ? null : lastName.Trim();
    }

    /// <summary>The eShopOnWeb user name, taken from the bearer token and never from the request body.</summary>
    public string UserName { get; }

    /// <summary>
    /// Handle of the plan to subscribe to. When omitted the configured default plan handle is used.
    /// </summary>
    public string? PlanHandle { get; }

    /// <summary>
    /// Caller-supplied key that makes a retry of the same logical subscribe request return the
    /// original subscription instead of creating a second one. Optional: the service also
    /// de-duplicates on "this user already holds this plan".
    /// </summary>
    public string? IdempotencyKey { get; }

    /// <summary>Optional shopper first name, used only when the billing customer is created.</summary>
    public string? FirstName { get; }

    /// <summary>Optional shopper last name, used only when the billing customer is created.</summary>
    public string? LastName { get; }
}
