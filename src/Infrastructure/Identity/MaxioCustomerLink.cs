using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

public sealed class MaxioCustomerLink
{
    public int Id { get; set; }
    public required string UserId { get; set; }
    public required string CustomerReference { get; set; }
    public int? MaxioCustomerId { get; set; }
    public required string Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
