namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// What the create-subscription handler actually acts on: the plan from the request body, plus the
/// user name lifted from the bearer token.
/// </summary>
/// <remarks>
/// Kept separate from <see cref="CreateSubscriptionRequest"/> so that the user name is structurally
/// impossible to supply from the wire. A caller cannot enroll anyone but themselves.
/// </remarks>
public class CreateSubscriptionCommand : BaseRequest
{
    public CreateSubscriptionCommand(string userName, string? planHandle)
    {
        UserName = userName;
        PlanHandle = planHandle;
    }

    public string UserName { get; }

    public string? PlanHandle { get; }
}
