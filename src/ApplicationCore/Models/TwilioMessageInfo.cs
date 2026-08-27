using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models;

/// <summary>
/// eShop's view of a Twilio Message resource (api.v2010.account.message).
/// </summary>
public class TwilioMessageInfo
{
    public string Sid { get; set; } = string.Empty;
    public string? Status { get; set; }
    public string? To { get; set; }
    public string? From { get; set; }
    public string? Body { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset? DateCreated { get; set; }
    public DateTimeOffset? DateSent { get; set; }
}
