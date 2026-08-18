using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// One message as the provider records it, used to reconcile the provider's view against eShop's.
/// </summary>
public class ProviderMessageRecord
{
    public ProviderMessageRecord(string sid, string? to, string? from, string? status, DateTimeOffset? dateSent)
    {
        Sid = sid;
        To = to;
        From = from;
        Status = status;
        DateSent = dateSent;
    }

    public string Sid { get; }
    public string? To { get; }
    public string? From { get; }
    public string? Status { get; }
    public DateTimeOffset? DateSent { get; }
}
