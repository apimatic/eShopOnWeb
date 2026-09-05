namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// Everything needed to enroll the caller in a plan. <see cref="UserReference"/> is the
/// eShopOnWeb user's stable, unique identifier (their username) and is used as the Maxio
/// customer's "reference" - the field that makes customer creation idempotent.
/// </summary>
public class SubscribeRequest
{
    public required string UserReference { get; init; }
    public required string Email { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required string PlanHandle { get; init; }
}
