using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The application service behind the invoicing endpoints. It orchestrates the order/basket model,
/// eShop's own bill records, and the external provider, and it enforces shopper scoping. Results carry
/// a status the API layer maps onto HTTP; the amount billed always comes from the order.
/// </summary>
public interface IInvoicingService
{
    /// <summary>Place an order for the shopper from catalog items and quantities, reusing the app's order model.</summary>
    Task<OperationResult<PlacedOrderResult>> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> lines, CancellationToken cancellationToken = default);

    /// <summary>Raise a bill with the provider for one of the caller's orders, due on the given date.</summary>
    Task<OperationResult<RaisedInvoiceResult>> RaiseInvoiceAsync(int orderId, DateOnly dueDate, CallerContext caller, CancellationToken cancellationToken = default);

    /// <summary>Read a bill the caller is allowed to see, including live provider state and (once issued) how to pay it.</summary>
    Task<OperationResult<InvoiceDetailsResult>> GetInvoiceAsync(string invoiceId, CallerContext caller, CancellationToken cancellationToken = default);

    /// <summary>Correct the due date/customer details of a not-yet-issued bill the caller is allowed to correct.</summary>
    Task<OperationResult<InvoiceDetailsResult>> CorrectInvoiceAsync(string invoiceId, InvoiceCorrection correction, CallerContext caller, CancellationToken cancellationToken = default);

    /// <summary>Operator action: put a bill to the shopper.</summary>
    Task<OperationResult<InvoiceDetailsResult>> IssueInvoiceAsync(string invoiceId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: withdraw a bill so it is no longer payable.</summary>
    Task<OperationResult<InvoiceDetailsResult>> WithdrawInvoiceAsync(string invoiceId, CancellationToken cancellationToken = default);

    /// <summary>The caller's own bills, each showing where it has got to.</summary>
    Task<OperationResult<IReadOnlyList<InvoiceSummaryResult>>> GetInvoicesForShopperAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: reconcile the provider's record of bills in a range against eShop's own.</summary>
    Task<OperationResult<ReconciliationReport>> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
