using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

/// <summary>
/// Local mapping between an eShopOnWeb user and their Maxio (Advanced Billing) customer.
/// The Maxio customer <see cref="Reference"/> is the durable idempotency key and is set
/// to the eShopOnWeb user's username (email). This table is a cache; Maxio is the system
/// of record.
/// </summary>
public class MaxioCustomer : BaseEntity, IAggregateRoot
{
    public string UserId { get; set; } = string.Empty;
    public int MaxioCustomerId { get; set; }
    public string Reference { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
