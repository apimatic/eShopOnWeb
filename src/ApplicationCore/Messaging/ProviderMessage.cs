using System;

namespace Microsoft.eShopWeb.ApplicationCore.Messaging;

/// <summary>
/// The provider's own record of a single message.
/// </summary>
public class ProviderMessage
{
    public string Sid { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? To { get; set; }
    public string? From { get; set; }
    public string? Body { get; set; }
    public DateTimeOffset? DateCreated { get; set; }
    public DateTimeOffset? DateSent { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
}
