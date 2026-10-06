namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// The values accepted by the Maxio <c>payment_collection_method</c> field
/// (maxio-spec: components/schemas/Collection-Method.yaml).
/// </summary>
public static class PaymentCollectionMethods
{
    public const string Automatic = "automatic";

    /// <summary>
    /// Invoice based collection: Maxio issues an invoice instead of capturing a card at signup.
    /// This is what lets eShopOnWeb enroll a shopper on a plan that does not require a payment
    /// method, without any card capture or 3-DS step.
    /// </summary>
    public const string Remittance = "remittance";

    public const string Prepaid = "prepaid";
}
