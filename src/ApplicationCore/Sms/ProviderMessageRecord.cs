using System;

namespace Microsoft.eShopWeb.ApplicationCore.Sms;

/// <summary>
/// One message as the provider's own records describe it — used by reconciliation to line the
/// provider's ledger up against what eShop believes it sent.
/// </summary>
public class ProviderMessageRecord
{
    public ProviderMessageRecord(string sid, string? status, string? to, string? from, DateTimeOffset? dateSent, int? errorCode, string? errorMessage)
    {
        Sid = sid;
        Status = status;
        To = to;
        From = from;
        DateSent = dateSent;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public string Sid { get; }

    public string? Status { get; }

    public string? To { get; }

    public string? From { get; }

    public DateTimeOffset? DateSent { get; }

    public int? ErrorCode { get; }

    public string? ErrorMessage { get; }
}
