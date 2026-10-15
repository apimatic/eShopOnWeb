using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

/// <summary>
/// Maps an eShopOnWeb user to their Maxio customer. Keyed on the user so only one customer is ever created.
/// </summary>
public class MaxioCustomerLink
{
    public string UserKey { get; set; } = string.Empty;
    public int? MaxioCustomerId { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
