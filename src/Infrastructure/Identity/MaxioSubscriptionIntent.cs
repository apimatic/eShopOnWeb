using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

public sealed class MaxioSubscriptionIntent
{
    public Guid Id { get; set; }
    public required string UserId { get; set; }
    public required string ProductHandle { get; set; }
    public required string CustomerReference { get; set; }
    public required string SubscriptionReference { get; set; }
    public long? MaxioSubscriptionId { get; set; }
    public required string Status { get; set; }
    public int? LastProviderStatusCode { get; set; }
    public string? LastErrorCategory { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
