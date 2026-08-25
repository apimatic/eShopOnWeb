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
using Microsoft.eShopWeb.PublicApi.PayPal;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IReadRepository<OrderPayment>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                       AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (string from,
                   string to,
                   IReadRepository<OrderPayment> paymentRepo,
                   IPayPalClient paypal,
                   ILogger<ReconciliationEndpoint> logger) =>
            {
                if (!DateTimeOffset.TryParse(from, out var fromDate) || !DateTimeOffset.TryParse(to, out var toDate))
                    return Results.BadRequest(new { error = "from and to must be ISO-8601 date-times." });

                if (toDate <= fromDate)
                    return Results.BadRequest(new { error = "to must be after from." });

                // Fetch PayPal transactions
                List<PayPalTransactionDetail> paypalTx;
                try
                {
                    paypalTx = await paypal.SearchTransactionsAllPagesAsync(fromDate, toDate);
                }
                catch (PayPalException ex)
                {
                    logger.LogError(ex, "PayPal transaction search failed");
                    return Results.UnprocessableEntity(new { error = ex.Message, detail = ex.PayPalErrorBody });
                }

                // Fetch eShop payments in range
                var allPayments = await paymentRepo.ListAsync();
                var paymentsInRange = allPayments
                    .Where(p => p.CreatedAt >= fromDate && p.CreatedAt <= toDate)
                    .ToList();

                // Build lookup maps
                var paypalByAuthId = paypalTx
                    .Where(t => t.TransactionInfo?.PaypalReferenceId != null)
                    .GroupBy(t => t.TransactionInfo!.PaypalReferenceId!)
                    .ToDictionary(g => g.Key, g => g.First());

                var paypalByTxId = paypalTx
                    .Where(t => t.TransactionInfo?.TransactionId != null)
                    .GroupBy(t => t.TransactionInfo!.TransactionId!)
                    .ToDictionary(g => g.Key, g => g.First());

                // Reconcile
                var matched = new List<ReconciliationMatch>();
                var eShopOnly = new List<EShopOnlyRecord>();
                var paypalOnly = new List<PayPalOnlyRecord>();

                var usedPaypalTxIds = new HashSet<string>();

                foreach (var payment in paymentsInRange)
                {
                    PayPalTransactionDetail? match = null;
                    if (payment.CaptureId != null && paypalByTxId.TryGetValue(payment.CaptureId, out var byCapture))
                        match = byCapture;
                    else if (payment.AuthorizationId != null && paypalByAuthId.TryGetValue(payment.AuthorizationId, out var byAuth))
                        match = byAuth;

                    if (match != null)
                    {
                        var txId = match.TransactionInfo?.TransactionId ?? string.Empty;
                        usedPaypalTxIds.Add(txId);
                        matched.Add(new ReconciliationMatch(
                            payment.OrderId,
                            payment.Status.ToString(),
                            payment.Amount,
                            payment.CapturedAmount,
                            txId,
                            match.TransactionInfo?.TransactionStatus,
                            match.TransactionInfo?.TransactionAmount?.Value
                        ));
                    }
                    else
                    {
                        eShopOnly.Add(new EShopOnlyRecord(
                            payment.OrderId,
                            payment.Status.ToString(),
                            payment.Amount,
                            payment.AuthorizationId,
                            payment.CaptureId
                        ));
                    }
                }

                // PayPal-only: in PayPal but not matched to an eShop payment
                foreach (var tx in paypalTx)
                {
                    var txId = tx.TransactionInfo?.TransactionId ?? string.Empty;
                    if (!usedPaypalTxIds.Contains(txId))
                    {
                        paypalOnly.Add(new PayPalOnlyRecord(
                            txId,
                            tx.TransactionInfo?.TransactionStatus,
                            tx.TransactionInfo?.TransactionAmount?.Value,
                            tx.TransactionInfo?.TransactionInitiationDate,
                            tx.TransactionInfo?.CustomField
                        ));
                    }
                }

                return Results.Ok(new ReconciliationResponse(
                    from, to,
                    matched, eShopOnly, paypalOnly
                ));
            })
            .Produces<ReconciliationResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(ReconciliationRequest request, IReadRepository<OrderPayment> service)
        => Task.FromResult(Results.StatusCode(501));
}

public class ReconciliationRequest : BaseRequest { }

public record ReconciliationMatch(
    int EShopOrderId,
    string EShopPaymentStatus,
    decimal EShopAmount,
    decimal? EShopCapturedAmount,
    string PayPalTransactionId,
    string? PayPalTransactionStatus,
    string? PayPalAmount
);

public record EShopOnlyRecord(
    int EShopOrderId,
    string PaymentStatus,
    decimal Amount,
    string? AuthorizationId,
    string? CaptureId
);

public record PayPalOnlyRecord(
    string TransactionId,
    string? Status,
    string? Amount,
    string? TransactionDate,
    string? CustomField
);

public record ReconciliationResponse(
    string From,
    string To,
    List<ReconciliationMatch> Matched,
    List<EShopOnlyRecord> EShopOnly,
    List<PayPalOnlyRecord> PayPalOnly
);
