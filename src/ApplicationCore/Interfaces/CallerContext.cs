namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The authenticated caller as the application layer needs to see them: their identity (which equals
/// the order/bill owner id) and whether they act as an operator. Shopper-scoped operations use
/// <see cref="CanAccess"/> so that a shopper only ever reaches their own data while an operator may
/// reach anyone's.
/// </summary>
public sealed record CallerContext(string UserName, bool IsAdmin)
{
    /// <summary>True when the caller owns the data, or is an operator acting on anyone's data.</summary>
    public bool CanAccess(string ownerId) => IsAdmin || string.Equals(ownerId, UserName, System.StringComparison.Ordinal);
}
