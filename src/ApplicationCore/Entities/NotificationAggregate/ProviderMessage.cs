using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public class ProviderMessage
{
    public string? Sid { get; init; }
    public string? Status { get; init; }
    public int? ErrorCode { get; init; }
    public string? Body { get; init; }
    public string? From { get; init; }
    public string? To { get; init; }
    public DateTimeOffset? DateCreated { get; init; }
    public DateTimeOffset? DateSent { get; init; }
    public DateTimeOffset? DateUpdated { get; init; }
}

public class ProviderSendResult
{
    public bool Accepted { get; init; }
    public ProviderMessage? Message { get; init; }
    public int? ErrorCode { get; init; }
    public string? ErrorStatus { get; init; }
}

public class CreateProviderMessageRequest
{
    public required string To { get; init; }
    public required string Body { get; init; }
    public DateTimeOffset? SendAt { get; init; }
}
