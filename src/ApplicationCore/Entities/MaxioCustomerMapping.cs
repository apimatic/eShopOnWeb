using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class MaxioCustomerMapping : BaseEntity
{
    public string UserId { get; set; } = null!;
    public int MaxioCustomerId { get; set; }
    public string Reference { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
}
