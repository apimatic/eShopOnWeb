namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Identifies this eShop deployment so the bills it raises can be told apart from bills raised by other
/// activity on the shared provider account (including other eShop deployments). The tag is stamped into
/// the provider's merchant-customer-id — the one app-owned value that round-trips to the provider's
/// list projection — and is used during reconciliation to recognise this deployment's own bills.
/// </summary>
public interface IInvoicingInstance
{
    string Tag { get; }
}
