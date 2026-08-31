using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>
/// Orchestrates the invoicing flows on top of the eShop order model and the Visa provider seam.
/// Keeps the HTTP endpoints thin: they translate identity and route/body into calls here and map
/// the returned <see cref="OperationResult{T}"/> outcome to a status code.
/// </summary>
public interface IInvoiceAppService
{
    /// <summary>Place an order for the shopper from catalog items (prices are taken server-side).</summary>
    Task<OperationResult<CreateOrderResponse>> PlaceOrderAsync(string buyerId, CreateOrderRequest request, CancellationToken cancellationToken = default);

    /// <summary>Raise a bill with the provider for one of the shopper's own orders.</summary>
    Task<OperationResult<InvoiceDto>> RaiseInvoiceAsync(string buyerId, RaiseInvoiceRequest request, CancellationToken cancellationToken = default);

    /// <summary>Read one of the shopper's own bills, refreshed from the provider.</summary>
    Task<OperationResult<InvoiceDto>> GetInvoiceAsync(string buyerId, string invoiceId, CancellationToken cancellationToken = default);

    /// <summary>Correct the due date or customer details of one of the shopper's own bills, while it still can be.</summary>
    Task<OperationResult<InvoiceDto>> CorrectInvoiceAsync(string buyerId, CorrectInvoiceRequest request, CancellationToken cancellationToken = default);

    /// <summary>Operator action: put a bill to the shopper (issue it).</summary>
    Task<OperationResult<InvoiceDto>> IssueInvoiceAsync(string invoiceId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: withdraw a bill so it can no longer be paid.</summary>
    Task<OperationResult<InvoiceDto>> WithdrawInvoiceAsync(string invoiceId, CancellationToken cancellationToken = default);

    /// <summary>List the shopper's own bills.</summary>
    Task<IReadOnlyList<InvoiceSummaryDto>> GetMyInvoicesAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: reconcile the provider's record of bills in a range against eShop's.</summary>
    Task<OperationResult<ReconciliationReportDto>> ReconcileAsync(string from, string to, CancellationToken cancellationToken = default);
}
