using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

/// <summary>
/// A short-lived claim on an operation (e.g. "subscribe shopper X to plan Y"). The primary key is the claim key,
/// so the store itself refuses a second concurrent claim before the billing system is called.
/// </summary>
public class OperationClaim
{
    public string Key { get; set; } = string.Empty;
    public DateTimeOffset ClaimedAt { get; set; }
}
