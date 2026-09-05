using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

/// <summary>Durable link between an eShopOnWeb identity and its Maxio customer.</summary>
public class MaxioCustomerLink
{
    public int Id { get; set; }
    public required string UserId { get; set; }
    public required int MaxioCustomerId { get; set; }
    public required string CustomerReference { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
