using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Invoicing;

/// <summary>
/// A bill as reported to a caller: eShop's own record (when one exists) together with the live state
/// the provider owns. <see cref="Local"/> is null only when an operator reads a bill that eShop did
/// not raise.
/// </summary>
public record InvoiceDetail(Invoice? Local, ProviderInvoice Provider);
