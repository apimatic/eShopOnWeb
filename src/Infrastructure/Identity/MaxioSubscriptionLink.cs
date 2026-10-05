using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

/// <summary>
/// The link between an eShopOnWeb user's subscription to a plan and the Maxio subscription.
/// Composite primary key (user id, plan handle) so a double-subscribe can never create a second row.
/// </summary>
public class MaxioSubscriptionLink
{
    /// <summary>
    /// The eShopOnWeb user id (AspNetUsers.Id).
    /// </summary>
    public string EShopUserId { get; set; } = default!;

    /// <summary>
    /// The plan (Maxio product) handle.
    /// </summary>
    public string PlanHandle { get; set; } = default!;

    /// <summary>
    /// The Maxio customer that holds the subscription.
    /// </summary>
    public int MaxioCustomerId { get; set; }

    /// <summary>
    /// The Maxio subscription id, once created. Null while the subscribe is in flight.
    /// </summary>
    public int? MaxioSubscriptionId { get; set; }

    /// <summary>
    /// The subscription reference registered at Maxio (deterministic from user id + plan handle).
    /// </summary>
    public string MaxioReference { get; set; } = default!;

    /// <summary>
    /// The provider subscription state last seen.
    /// </summary>
    public string? State { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}