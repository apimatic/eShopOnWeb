using System;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.Maxio;

/// <summary>
/// Persists the mapping between an eShopOnWeb user and their Maxio customer so that customer
/// provisioning is idempotent.
/// </summary>
public class MaxioCustomerMapping : BaseEntity, IAggregateRoot
{
    public MaxioCustomerMapping(string applicationUserId, int maxioCustomerId, string maxioCustomerReference)
    {
        ApplicationUserId = applicationUserId;
        MaxioCustomerId = maxioCustomerId;
        MaxioCustomerReference = maxioCustomerReference;
        CreatedAtUtc = DateTime.UtcNow;
    }

    private MaxioCustomerMapping()
    {
        // Required by EF Core
    }

    public string ApplicationUserId { get; private set; } = string.Empty;
    public int MaxioCustomerId { get; private set; }
    public string MaxioCustomerReference { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
}
