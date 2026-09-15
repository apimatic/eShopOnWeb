using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

/// <summary>
/// Durable application-side correlation for an enrollment sent to Maxio.
/// </summary>
public class SubscriptionEnrollment
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public string MaxioCustomerReference { get; set; } = string.Empty;
    public string MaxioSubscriptionReference { get; set; } = string.Empty;
    public int? MaxioCustomerId { get; set; }
    public int? MaxioSubscriptionId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
}
