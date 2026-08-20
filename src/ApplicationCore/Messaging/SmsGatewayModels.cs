using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Messaging;

public sealed record ProviderMessage(
    string? Sid,
    string Status,
    int? ErrorCode,
    string? ErrorMessage,
    string? Body,
    string? From,
    string? To,
    string? DateSent);

public abstract record LookupResult
{
    public sealed record Usable(string CanonicalPhoneNumber) : LookupResult;
    public sealed record Unusable(string Message) : LookupResult;
}

public abstract record GatewayResult
{
    public sealed record Ok(ProviderMessage Message) : GatewayResult;
    public sealed record Failed(string Message, int? HttpStatus) : GatewayResult;
    public sealed record Unknown(string Message) : GatewayResult;
}

public sealed record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    bool Truncated,
    IReadOnlyList<ReconciliationRow> Matched,
    IReadOnlyList<ReconciliationRow> ProviderOnly,
    IReadOnlyList<ReconciliationRow> EshopOnly);

public sealed record ReconciliationRow(
    int? NotificationId,
    string? ProviderSid,
    string? Status,
    string? Body,
    string? DateSent,
    string? Direction);

public sealed record ProviderMessageList(IReadOnlyList<ProviderMessage> Messages, bool Truncated);
