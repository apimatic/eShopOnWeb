using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class ReconciliationEndpoint : IEndpoint<IResult, IPayPalService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                       AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (string from, string to,
                   IRepository<OrderPayment> paymentRepo,
                   IPayPalService paypal) =>
            {
                if (!DateTimeOffset.TryParse(from, null, System.Globalization.DateTimeStyles.RoundtripKind, out var fromDate))
                    return Results.BadRequest("Invalid 'from' date. Use ISO-8601 format.");
                if (!DateTimeOffset.TryParse(to, null, System.Globalization.DateTimeStyles.RoundtripKind, out var toDate))
                    return Results.BadRequest("Invalid 'to' date. Use ISO-8601 format.");
                if (fromDate >= toDate)
                    return Results.BadRequest("'from' must be before 'to'.");

                // Fetch PayPal transactions for the range
                var paypalTxns = await paypal.SearchTransactionsAsync(fromDate, toDate);

                // Fetch local payments in the range (include refunds for matching)
                var allPayments = await paymentRepo.ListAsync(new AllOrderPaymentsWithRefundsSpec());
                var localPayments = allPayments
                    .Where(p => p.CreatedAt >= fromDate && p.CreatedAt <= toDate)
                    .ToList();

                // Build lookup sets for matching
                var localPayPalOrderIds = new HashSet<string>(
                    localPayments.Select(p => p.PayPalOrderId), StringComparer.OrdinalIgnoreCase);
                var localAuthIds = new HashSet<string>(
                    localPayments.Where(p => p.PayPalAuthorizationId != null)
                                 .Select(p => p.PayPalAuthorizationId!), StringComparer.OrdinalIgnoreCase);
                var localCaptureIds = new HashSet<string>(
                    localPayments.Where(p => p.PayPalCaptureId != null)
                                 .Select(p => p.PayPalCaptureId!), StringComparer.OrdinalIgnoreCase);
                var localRefundIds = new HashSet<string>(
                    localPayments.SelectMany(p => p.Refunds)
                                 .Select(r => r.PayPalRefundId), StringComparer.OrdinalIgnoreCase);

                var matched = new List<ReconciliationMatch>();
                var paypalOnly = new List<PayPalTxnDto>();

                foreach (var txn in paypalTxns)
                {
                    var tid = txn.TransactionId;
                    var rid = txn.PayPalReferenceId ?? "";

                    // Try to find a matching local payment
                    var matchedPayment = localPayments.FirstOrDefault(p =>
                        string.Equals(p.PayPalOrderId, tid, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(p.PayPalOrderId, rid, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(p.PayPalAuthorizationId, tid, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(p.PayPalAuthorizationId, rid, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(p.PayPalCaptureId, tid, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(p.PayPalCaptureId, rid, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrEmpty(txn.CustomField) &&
                         int.TryParse(txn.CustomField, out var cf) &&
                         p.OrderId == cf));

                    bool refundMatch = !string.IsNullOrEmpty(tid) && localRefundIds.Contains(tid);

                    if (matchedPayment != null || refundMatch)
                    {
                        matched.Add(new ReconciliationMatch
                        {
                            PayPalTransactionId = tid,
                            PayPalReferenceId = txn.PayPalReferenceId,
                            LocalOrderId = matchedPayment?.OrderId,
                            Amount = txn.Amount,
                            Currency = txn.Currency,
                            Status = txn.Status,
                            EventCode = txn.EventCode,
                            InitiationDate = txn.InitiationDate
                        });
                    }
                    else
                    {
                        paypalOnly.Add(new PayPalTxnDto
                        {
                            TransactionId = tid,
                            ReferenceId = txn.PayPalReferenceId,
                            Amount = txn.Amount,
                            Currency = txn.Currency,
                            Status = txn.Status,
                            EventCode = txn.EventCode,
                            InitiationDate = txn.InitiationDate
                        });
                    }
                }

                // Local orders with no matching PayPal transaction
                var matchedOrderIds = matched
                    .Where(m => m.LocalOrderId.HasValue)
                    .Select(m => m.LocalOrderId!.Value)
                    .ToHashSet();

                var eShopOnly = localPayments
                    .Where(p => !matchedOrderIds.Contains(p.OrderId))
                    .Select(p => new LocalOrderDto
                    {
                        OrderId = p.OrderId,
                        PayPalOrderId = p.PayPalOrderId,
                        Amount = p.Amount,
                        Currency = p.Currency,
                        Status = p.Status.ToString(),
                        CreatedAt = p.CreatedAt
                    })
                    .ToList();

                return Results.Ok(new
                {
                    From = fromDate,
                    To = toDate,
                    MatchedTransactions = matched,
                    PayPalOnlyTransactions = paypalOnly,
                    EShopOnlyOrders = eShopOnly,
                    Summary = new
                    {
                        TotalPayPalTransactions = paypalTxns.Count,
                        Matched = matched.Count,
                        PayPalOnly = paypalOnly.Count,
                        EShopOnly = eShopOnly.Count
                    }
                });
            })
            .WithTags("ReconciliationEndpoints");
    }

    public Task<IResult> HandleAsync(IPayPalService repository)
        => throw new NotImplementedException();
}

public class ReconciliationMatch
{
    public string PayPalTransactionId { get; set; } = "";
    public string? PayPalReferenceId { get; set; }
    public int? LocalOrderId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "";
    public string? Status { get; set; }
    public string? EventCode { get; set; }
    public DateTimeOffset? InitiationDate { get; set; }
}

public class PayPalTxnDto
{
    public string TransactionId { get; set; } = "";
    public string? ReferenceId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "";
    public string? Status { get; set; }
    public string? EventCode { get; set; }
    public DateTimeOffset? InitiationDate { get; set; }
}

public class LocalOrderDto
{
    public int OrderId { get; set; }
    public string PayPalOrderId { get; set; } = "";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}
