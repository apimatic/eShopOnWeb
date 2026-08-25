using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.Payment;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IRepository<PaymentRecord>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                       AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (string from, string to,
                   IRepository<PaymentRecord> paymentRepo,
                   IPayPalService payPalService) =>
            {
                var request = new ReconciliationRequest { From = from, To = to };
                return await HandleAsync(request, paymentRepo, payPalService);
            })
            .Produces<ReconciliationResponse>(200)
            .Produces(400)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(ReconciliationRequest request, IRepository<PaymentRecord> repo)
        => throw new System.NotSupportedException();

    private static async Task<IResult> HandleAsync(
        ReconciliationRequest request,
        IRepository<PaymentRecord> paymentRepo,
        IPayPalService payPalService)
    {
        if (!DateTimeOffset.TryParse(request.From, out var from))
            return Results.BadRequest(new { error = "Invalid 'from' date format. Use ISO-8601." });
        if (!DateTimeOffset.TryParse(request.To, out var to))
            return Results.BadRequest(new { error = "Invalid 'to' date format. Use ISO-8601." });

        IReadOnlyList<PayPalTransaction> transactions;
        try
        {
            transactions = await payPalService.GetTransactionsAsync(from, to);
        }
        catch (PayPalProviderException ex)
        {
            return Results.Problem(ex.Message, statusCode: 502);
        }
        catch (System.Text.Json.JsonException)
        {
            return Results.Problem("PayPal returned an unreadable response.", statusCode: 502);
        }

        var allPaymentRecords = await paymentRepo.ListAsync();

        var paypalIdIndex = new Dictionary<string, PaymentRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var pr in allPaymentRecords)
        {
            if (pr.PayPalOrderId != null) paypalIdIndex.TryAdd(pr.PayPalOrderId, pr);
            if (pr.AuthorizationId != null) paypalIdIndex.TryAdd(pr.AuthorizationId, pr);
            if (pr.CaptureId != null) paypalIdIndex.TryAdd(pr.CaptureId, pr);
        }

        var rows = new List<ReconciliationRow>();
        var matchedOrderIds = new HashSet<int>();

        foreach (var tx in transactions)
        {
            PaymentRecord? match = null;
            if (tx.TransactionId != null) paypalIdIndex.TryGetValue(tx.TransactionId, out match);
            if (match == null && tx.ReferenceId != null) paypalIdIndex.TryGetValue(tx.ReferenceId, out match);

            rows.Add(new ReconciliationRow
            {
                PayPalTransactionId = tx.TransactionId,
                PayPalReferenceId = tx.ReferenceId,
                Status = tx.Status,
                Amount = tx.Amount,
                FeeAmount = tx.FeeAmount,
                InitiationDate = tx.InitiationDate,
                EShopOrderId = match?.OrderId,
                MatchStatus = match != null ? "Matched" : "PayPal only"
            });

            if (match != null) matchedOrderIds.Add(match.OrderId);
        }

        foreach (var pr in allPaymentRecords)
        {
            if (!matchedOrderIds.Contains(pr.OrderId)
                && pr.Status != PaymentRecordStatus.AwaitingPayment
                && pr.PayPalOrderId != null)
            {
                rows.Add(new ReconciliationRow
                {
                    EShopOrderId = pr.OrderId,
                    PayPalTransactionId = null,
                    PayPalReferenceId = pr.CaptureId ?? pr.AuthorizationId ?? pr.PayPalOrderId,
                    Status = pr.Status.ToString(),
                    Amount = pr.CapturedAmount > 0 ? pr.CapturedAmount : null,
                    MatchStatus = "eShop only"
                });
            }
        }

        return Results.Ok(new ReconciliationResponse
        {
            From = request.From,
            To = request.To,
            Rows = rows,
            PayPalTransactionCount = transactions.Count,
            UnmatchedPayPalCount = rows.Count(r => r.MatchStatus == "PayPal only"),
            UnmatchedEShopCount = rows.Count(r => r.MatchStatus == "eShop only")
        });
    }
}
