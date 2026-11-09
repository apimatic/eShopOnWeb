using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

/// <summary>Links a shop account (by email) to its customer record at Stax.</summary>
public class ShopperStaxCustomer : BaseEntity, IAggregateRoot
{
    public string Email { get; set; } = string.Empty;
    public string StaxCustomerId { get; set; } = string.Empty;
}
