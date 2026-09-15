using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public sealed class ProviderMessageSnapshot
{
    public bool Succeeded { get; init; }
    public string? Sid { get; init; }
    public string? Status { get; init; }
    public string? Body { get; init; }
    public string? From { get; init; }
    public string? To { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTimeOffset? DateCreated { get; init; }
    public DateTimeOffset? DateSent { get; init; }
}
