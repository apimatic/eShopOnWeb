using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: lists PayPal's own record of transactions for a date range
/// (every page of the range) lined up against eShop orders, so a transaction only
/// one side knows about is visible.
/// </summary>
public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IOrderPaymentService orderPaymentService, CancellationToken ct) =>
            {
                var report = await orderPaymentService.GetReconciliationAsync(from, to, ct);

                var response = new ReconciliationResponse
                {
                    From = report.From,
                    To = report.To,
                    Transactions = report.Transactions.Select(ToDto).ToList(),
                    UnmatchedTransactions = report.UnmatchedTransactions.Select(ToDto).ToList(),
                    OrdersWithoutPayPalTransaction = report.OrdersWithoutPayPalTransaction,
                    Note = "PayPal transaction reporting can lag live activity (up to a few hours in sandbox); " +
                           "a very recent payment may legitimately be absent from PayPal's report."
                };
                return Results.Ok(response);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("OrderEndpoints");
    }

    private static ReconciliationTransactionDto ToDto(ReconciliationEntry entry) => new()
    {
        TransactionId = entry.Transaction.TransactionId,
        ReferenceId = entry.Transaction.ReferenceId,
        ReferenceIdType = entry.Transaction.ReferenceIdType,
        EventCode = entry.Transaction.EventCode,
        Amount = entry.Transaction.Amount,
        Currency = entry.Transaction.Currency,
        Fee = entry.Transaction.Fee,
        Status = entry.Transaction.Status,
        InvoiceId = entry.Transaction.InvoiceId,
        CustomField = entry.Transaction.CustomField,
        InitiationDate = entry.Transaction.InitiationDate,
        UpdatedDate = entry.Transaction.UpdatedDate,
        MatchedOrderId = entry.MatchedOrderId,
        MatchedPaymentId = entry.MatchedPaymentId
    };
}
