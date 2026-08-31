using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string SendingNumber { get; set; } = string.Empty;
    public int ProviderMessageCount { get; set; }
    public int LocalMessageCount { get; set; }
    public int MatchedCount { get; set; }

    /// <summary>Messages the provider knows about from our sending number that eShop has no record of.</summary>
    public List<ProviderMessageDto> MissingFromLocal { get; set; } = new();

    /// <summary>Messages eShop believes it sent that the provider has no record of in range.</summary>
    public List<LocalMessageDto> MissingFromProvider { get; set; } = new();

    /// <summary>Messages both sides know about, but whose delivery outcome differs.</summary>
    public List<StatusMismatchDto> StatusMismatches { get; set; } = new();
}

public class ProviderMessageDto
{
    public string MessageSid { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int? ErrorCode { get; set; }
    public DateTimeOffset? DateSent { get; set; }
}

public class LocalMessageDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string? MessageSid { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public class StatusMismatchDto
{
    public int NotificationId { get; set; }
    public string MessageSid { get; set; } = string.Empty;
    public string LocalStatus { get; set; } = string.Empty;
    public string ProviderStatus { get; set; } = string.Empty;
}
