using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class UserMaxioCustomer : BaseEntity, IAggregateRoot
{
    public string ApplicationUserId { get; set; } = string.Empty;
    public int MaxioCustomerId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
