using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

/// <summary>
/// A durable enrollment intent. Maxio remains the billing system of record; this row
/// prevents concurrent application requests from creating the same enrollment twice.
/// </summary>
public class SubscriptionEnrollment
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public long? MaxioSubscriptionId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
