using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

/// <summary>
/// Local link between an eShopOnWeb user and the Maxio Advanced Billing customer created for it.
/// The primary key is the Maxio customer reference, which this application sets to the eShop user id.
/// Inserting a row is the duplicate-prevention claim for concurrent customer creation.
/// </summary>
public class MaxioCustomerLink : IAggregateRoot
{
    /// <summary>
    /// The Maxio customer reference value (== the eShopOnWeb user id). Primary key of the claim.
    /// </summary>
    public string MaxioReference { get; set; } = string.Empty;

    /// <summary>
    /// The numeric Maxio customer id, recorded once the customer exists. 0 while the claim is pending.
    /// </summary>
    public int MaxioCustomerId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}