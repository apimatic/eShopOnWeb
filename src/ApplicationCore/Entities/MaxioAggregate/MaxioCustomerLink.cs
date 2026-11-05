using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.MaxioAggregate;

public enum MaxioLinkStatus
{
    /// <summary>Claim taken; the Maxio call is in flight (or the process died before settling it).</summary>
    Claimed = 0,
    /// <summary>Maxio confirmed the entity; the Maxio id is recorded.</summary>
    Created = 1,
    /// <summary>The write may or may not have reached Maxio; must be settled by a re-read.</summary>
    Unknown = 2
}

/// <summary>
/// Maps an eShopOnWeb user to its Maxio customer. The primary key (<see cref="UserKey"/>) is the
/// claim that stops a second concurrent "ensure customer" from reaching Maxio.
/// </summary>
public class MaxioCustomerLink
{
    public string UserKey { get; set; } = string.Empty;
    public int? MaxioCustomerId { get; set; }
    public MaxioLinkStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
