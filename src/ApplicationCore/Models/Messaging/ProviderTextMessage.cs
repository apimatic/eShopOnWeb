using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Messaging;

/// <summary>The provider's own record of a message, as listed for reconciliation.</summary>
public class ProviderTextMessage
{
    public string? ProviderMessageId { get; set; }
    public string? To { get; set; }
    public string? From { get; set; }
    public string? Status { get; set; }
    public string? DateSent { get; set; }
    public string? Body { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
}
