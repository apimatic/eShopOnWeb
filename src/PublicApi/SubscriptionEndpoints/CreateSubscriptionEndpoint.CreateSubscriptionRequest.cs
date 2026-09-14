using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to (e.g. "eshop-pro"). Plan handles are stable across
    /// sandbox re-seeds, numeric Maxio ids are not.
    /// </summary>
    public string? PlanHandle { get; set; }

    /// <summary>Optional first name for the Maxio customer record (falls back to the email).</summary>
    public string? FirstName { get; set; }

    /// <summary>Optional last name for the Maxio customer record (falls back to the email).</summary>
    public string? LastName { get; set; }
}
