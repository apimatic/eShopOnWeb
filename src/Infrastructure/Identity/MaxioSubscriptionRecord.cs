using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

public sealed class MaxioSubscriptionRecord
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public int MaxioCustomerId { get; set; }
    public int MaxioSubscriptionId { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
