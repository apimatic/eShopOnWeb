using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models;

/// <summary>
/// The provider's own record of a message.
/// </summary>
public class SmsMessageRecord
{
    public string Sid { get; set; } = string.Empty;
    public string? To { get; set; }
    public string? From { get; set; }
    public string? Status { get; set; }
    public DateTimeOffset? DateSent { get; set; }
    public DateTimeOffset? DateCreated { get; set; }
}
