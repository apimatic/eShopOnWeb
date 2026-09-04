using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public sealed class MaxioCustomer
{
    public int Id { get; set; }
    public required string UserId { get; set; }
    public required string Reference { get; set; }
    public required int MaxioCustomerId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
