using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

/// <summary>
/// A claim taken (by primary key) before a billing write is sent to Maxio, so a second caller
/// asking for the same operation is refused by the store instead of reaching Maxio.
/// </summary>
public class MaxioOperationClaim
{
    public string ClaimKey { get; set; } = string.Empty;
    public string Attempt { get; set; } = string.Empty;
    public string? Result { get; set; }
    public DateTime CreatedUtc { get; set; }
}
