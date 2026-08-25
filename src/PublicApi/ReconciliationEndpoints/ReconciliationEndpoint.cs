using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class ReconciliationEndpoint : IEndpoint<IResult, string, IPayPalService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (string from, string to,
                   IPayPalService payPal,
                   IRepository<PaymentRecord> paymentRepo,
                   CancellationToken ct) =>
            {
                if (!DateTimeOffset.TryParse(from, out var fromDate))
                    return Results.BadRequest(new { error = "Invalid 'from' date. Use ISO-8601 format." });
                if (!DateTimeOffset.TryParse(to, out var toDate))
                    return Results.BadRequest(new { error = "Invalid 'to' date. Use ISO-8601 format." });

                IReadOnlyList<PayPalTransactionRecord> transactions;
                try
                {
                    transactions = await payPal.GetTransactionsAsync(fromDate, toDate, ct);
                }
                catch (PayPalException ex)
                {
                    return Results.Problem(detail: ex.Message, statusCode: ex.StatusCode ?? 502, title: "Transaction search failed.");
                }

                // Load all payment records to match against
                var allPayments = await paymentRepo.ListAsync(ct);
                var byPayPalOrderId = allPayments
                    .Where(p => p.PayPalOrderId != null)
                    .ToDictionary(p => p.PayPalOrderId!, p => p);

                var rows = transactions.Select(t =>
                {
                    byPayPalOrderId.TryGetValue(t.PayPalReferenceId ?? "", out var match);
                    return new ReconciliationRow
                    {
                        PayPalTransactionId = t.TransactionId,
                        PayPalReferenceId = t.PayPalReferenceId,
                        TransactionStatus = t.Status,
                        Amount = t.Amount,
                        Currency = t.Currency,
                        Fee = t.Fee,
                        InitiationDate = t.InitiationDate,
                        EShopOrderId = match?.OrderId,
                        EShopPaymentStatus = match?.Status,
                        Matched = match != null
                    };
                }).ToList();

                return Results.Ok(new ReconciliationResponse
                {
                    From = from,
                    To = to,
                    TotalPayPalTransactions = transactions.Count,
                    UnmatchedCount = rows.Count(r => !r.Matched),
                    Rows = rows
                });
            })
            .Produces<ReconciliationResponse>()
            .Produces(400)
            .WithTags("ReconciliationEndpoints");
    }

    public Task<IResult> HandleAsync(string request, IPayPalService service)
        => throw new NotImplementedException();
}
