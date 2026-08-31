using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public sealed class InvoicingInstance : IInvoicingInstance
{
    public InvoicingInstance(string tag) => Tag = tag;

    public string Tag { get; }
}
