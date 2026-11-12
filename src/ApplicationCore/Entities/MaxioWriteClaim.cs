using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

/// <summary>
/// A claim taken (primary-key insert) before a non-idempotent Maxio write, so a second concurrent caller is refused.
/// </summary>
public class MaxioWriteClaim
{
    public string ClaimKey { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}
