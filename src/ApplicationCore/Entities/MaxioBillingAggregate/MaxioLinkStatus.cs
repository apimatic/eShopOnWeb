namespace Microsoft.eShopWeb.ApplicationCore.Entities.MaxioBillingAggregate;

public enum MaxioLinkStatus
{
    /// <summary>Row claimed before the provider call; the outcome is not (yet) known.</summary>
    Claimed = 0,
    /// <summary>The provider confirmed the write and its id is stored.</summary>
    Active = 1
}
