using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class MaxioCustomer : BaseEntity, IAggregateRoot
{
    public string ApplicationUserId { get; set; } = string.Empty;
    public int MaxioId { get; set; }
    public string MaxioReference { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
