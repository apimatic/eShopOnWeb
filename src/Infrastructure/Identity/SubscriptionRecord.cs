using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class SubscriptionRecord
{
    public long Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public string SubscriptionReference { get; set; } = string.Empty;
    public long? MaxioCustomerId { get; set; }
    public long? MaxioSubscriptionId { get; set; }
    public string Status { get; set; } = SubscriptionRecordStatus.Pending;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public byte[] Version { get; set; } = Array.Empty<byte>();
}

public static class SubscriptionRecordStatus
{
    public const string Pending = "pending";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
}
