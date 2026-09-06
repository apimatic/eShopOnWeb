using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class MaxioCustomerMapping : BaseEntity, IAggregateRoot
{
    public string ApplicationUserId { get; set; } = null!;
    public int MaxioCustomerId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
