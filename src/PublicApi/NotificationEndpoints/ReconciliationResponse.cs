using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ProviderMessageDto
{
    public string? Sid { get; set; }
    public string? Status { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? DateSent { get; set; }
    public string? DateCreated { get; set; }
    public string? To { get; set; }
    public string? From { get; set; }

    public static ProviderMessageDto FromSnapshot(SmsMessageSnapshot snapshot) => new()
    {
        Sid = snapshot.Sid,
        Status = snapshot.Status,
        ErrorCode = snapshot.ErrorCode,
        ErrorMessage = snapshot.ErrorMessage,
        DateSent = snapshot.DateSent,
        DateCreated = snapshot.DateCreated,
        To = snapshot.To,
        From = snapshot.From
    };
}

public class ReconciliationMatchDto
{
    public NotificationDto Local { get; set; } = new();
    public ProviderMessageDto Provider { get; set; } = new();
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ReconciliationResponse()
    {
    }

    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public string FromNumber { get; set; } = string.Empty;
    public bool Truncated { get; set; }
    public List<ReconciliationMatchDto> Matched { get; set; } = new();
    public List<ProviderMessageDto> ProviderOnly { get; set; } = new();
    public List<NotificationDto> EShopOnly { get; set; } = new();
}
