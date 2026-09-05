using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class MaxioCustomerMapping : BaseEntity
{
    public string UserId { get; set; } = string.Empty;
    public int MaxioCustomerId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
