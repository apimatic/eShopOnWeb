using System;

namespace Microsoft.eShopWeb.Infrastructure.Data;

/// <summary>
/// An in-flight claim on a Maxio write. The primary key (Key) is what refuses a second concurrent caller.
/// </summary>
public class MaxioClaim
{
    public string Key { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
