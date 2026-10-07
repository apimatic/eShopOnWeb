using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when an entity referenced in the billing system of record no
/// longer exists there (e.g. a subscription was deleted upstream).
/// </summary>
public class BillingEntityNotFoundException : Exception
{
    public BillingEntityNotFoundException(string message) : base(message) { }
}
