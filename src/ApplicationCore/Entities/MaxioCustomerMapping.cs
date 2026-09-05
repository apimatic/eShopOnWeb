using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

/// <summary>
/// Local cache of the eShopOnWeb user (by username, which doubles as email in this app) that has
/// already been provisioned as a Maxio customer, keyed by the deterministic reference used for
/// idempotent lookup on the Maxio side. This is a cache, not the source of truth: if it is lost
/// (e.g. the in-memory database restarts), Maxio's own reference lookup can always re-resolve it.
/// </summary>
public class MaxioCustomerMapping : BaseEntity, IAggregateRoot
{
    public string UserName { get; private set; }
    public string MaxioCustomerReference { get; private set; }
    public int MaxioCustomerId { get; private set; }

#pragma warning disable CS8618 // Required by Entity Framework
    private MaxioCustomerMapping() { }
#pragma warning restore CS8618

    public MaxioCustomerMapping(string userName, string maxioCustomerReference, int maxioCustomerId)
    {
        UserName = userName;
        MaxioCustomerReference = maxioCustomerReference;
        MaxioCustomerId = maxioCustomerId;
    }
}
