namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// Allowed values of the subscription payment_collection_method (see spec Collection-Method schema).
/// </summary>
public static class MaxioCollectionMethods
{
    public const string Automatic = "automatic";
    public const string Remittance = "remittance";
    public const string Prepaid = "prepaid";
    public const string Invoice = "invoice";
}
