using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

/// <summary>
/// Persisted link between an eShopOnWeb identity user and the Maxio Advanced Billing
/// customer created on their behalf. The UserId is the primary key so a concurrent
/// second claim for the same user is rejected by the store itself.
/// </summary>
public sealed class MaxioCustomerLink : IAggregateRoot
{
    /// <summary>
    /// The eShopOnWeb identity user id (ASP.NET Identity GUID string). Primary key.
    /// Also used as the customer reference in Maxio Advanced Billing.
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// The Maxio customer id returned when the customer was created or looked up.
    /// </summary>
    public int MaxioCustomerId { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }
}