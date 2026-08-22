using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Messaging;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public sealed class NotificationReconciliationReport
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public string FromNumber { get; init; } = string.Empty;
    public bool Truncated { get; init; }
    public IReadOnlyList<MatchedNotification> Matched { get; init; } = [];
    public IReadOnlyList<SmsMessageResult> ProviderOnly { get; init; } = [];
    public IReadOnlyList<ApplicationOnlyNotification> ApplicationOnly { get; init; } = [];
}

public sealed class MatchedNotification
{
    public int NotificationId { get; init; }
    public int OrderId { get; init; }
    public string? ProviderSid { get; init; }
    public string? ApplicationStatus { get; init; }
    public string? ProviderStatus { get; init; }
}

public sealed class ApplicationOnlyNotification
{
    public int NotificationId { get; init; }
    public int OrderId { get; init; }
    public string? ProviderSid { get; init; }
    public string? Status { get; init; }
    public string Kind { get; init; } = string.Empty;
}
