using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>
/// Orchestrates the ordering + invoicing flows behind the API endpoints: ownership and state rules,
/// mapping between the domain and the API contract, persistence, and calls to the invoicing provider.
/// Each method returns the <see cref="IResult"/> the endpoint should return. Failures talking to the
/// provider surface as <see cref="Microsoft.eShopWeb.ApplicationCore.Exceptions.InvoicingProviderException"/>
/// and are translated centrally by the exception middleware.
/// </summary>
public interface IInvoiceOrchestrator
{
    Task<IResult> CreateOrderAsync(CreateOrderRequest request, ClaimsPrincipal user, CancellationToken ct);

    Task<IResult> RaiseInvoiceAsync(int orderId, RaiseInvoiceForOrderRequest request, ClaimsPrincipal user, CancellationToken ct);

    Task<IResult> GetInvoiceAsync(string invoiceId, ClaimsPrincipal user, CancellationToken ct);

    Task<IResult> AmendInvoiceAsync(string invoiceId, AmendInvoiceRequest request, ClaimsPrincipal user, CancellationToken ct);

    // Operator (administrator) actions — they act on any shopper's bill.
    Task<IResult> IssueInvoiceAsync(string invoiceId, CancellationToken ct);

    Task<IResult> WithdrawInvoiceAsync(string invoiceId, CancellationToken ct);

    Task<IResult> GetMyInvoicesAsync(ClaimsPrincipal user, CancellationToken ct);

    Task<IResult> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
