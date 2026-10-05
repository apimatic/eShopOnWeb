using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

/// <summary>
/// The link between an eShopOnWeb user and the Maxio Advanced Billing customer ensured for them.
/// Primary key is the eShopOnWeb user id itself, so a double-ensure can never create a second row.
/// </summary>
public class MaxioCustomerLink
{
    /// <summary>
    /// The eShopOnWeb user id (AspNetUsers.Id).
    /// </summary>
    public string EShopUserId { get; set; } = default!;

    /// <summary>
    /// The Maxio customer id, once ensured. Null while the ensure is in flight.
    /// </summary>
    public int? MaxioCustomerId { get; set; }

    /// <summary>
    /// The customer reference registered at Maxio (deterministic from the user id).
    /// </summary>
    public string MaxioReference { get; set; } = default!;

    public DateTimeOffset CreatedAt { get; set; }
}