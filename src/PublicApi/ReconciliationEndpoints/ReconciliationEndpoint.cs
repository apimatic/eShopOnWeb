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
using EShopOrder = Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.Order;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IRepository<EShopOrder>>
{
    private readonly PayPalService _payPal;

    public ReconciliationEndpoint(PayPalService payPal)
    {
        _payPal = payPal;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (string from,
                   string to,
                   IRepository<EShopOrder> orderRepository,
                   CancellationToken ct) =>
            {
                if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
                    return Results.BadRequest("'from' and 'to' query parameters are required.");

                var transactions = await _payPal.GetTransactionsAsync(from, to, ct);

                if (!DateTimeOffset.TryParse(from, out var fromDate))
                    return Results.BadRequest("Invalid 'from' date format.");
                if (!DateTimeOffset.TryParse(to, out var toDate))
                    return Results.BadRequest("Invalid 'to' date format.");

                var allOrders = await orderRepository.ListAsync(ct);
                var ordersByPayPalId = new Dictionary<string, EShopOrder>(StringComparer.OrdinalIgnoreCase);
                foreach (var o in allOrders)
                {
                    if (!string.IsNullOrEmpty(o.PayPalOrderId))
                        ordersByPayPalId[o.PayPalOrderId] = o;
                    if (!string.IsNullOrEmpty(o.PayPalCaptureId))
                        ordersByPayPalId[o.PayPalCaptureId] = o;
                }

                var rows = new List<ReconciliationRow>();

                foreach (var txn in transactions)
                {
                    var txnInfo = txn.TransactionInfo;
                    var txnId = txnInfo?.TransactionId;

                    EShopOrder? matchedOrder = null;
                    if (txnId != null) ordersByPayPalId.TryGetValue(txnId, out matchedOrder);

                    rows.Add(new ReconciliationRow
                    {
                        PayPalTransactionId = txnId,
                        Amount = txnInfo?.TransactionAmount?.Value,
                        Currency = txnInfo?.TransactionAmount?.CurrencyCode,
                        Fee = txnInfo?.FeeAmount?.Value,
                        Status = txnInfo?.TransactionStatus,
                        Date = txnInfo?.TransactionInitiationDate,
                        EShopOrderId = matchedOrder?.Id,
                        EShopOrderStatus = matchedOrder?.Status.ToString(),
                        Matched = matchedOrder != null
                    });
                }

                // Flag PayPal-known transactions not in eShop
                var unmatchedPayPal = rows.Where(r => !r.Matched).ToList();

                // Flag eShop orders with PayPalCaptureId in date range not in PayPal results
                var payPalTxnIds = new HashSet<string>(
                    transactions.Select(t => t.TransactionInfo?.TransactionId ?? "").Where(id => id != ""),
                    StringComparer.OrdinalIgnoreCase);

                var unmatchedEShop = allOrders
                    .Where(o => !string.IsNullOrEmpty(o.PayPalCaptureId) &&
                                !payPalTxnIds.Contains(o.PayPalCaptureId) &&
                                o.OrderDate >= fromDate && o.OrderDate <= toDate)
                    .Select(o => new ReconciliationRow
                    {
                        PayPalTransactionId = o.PayPalCaptureId,
                        Amount = o.CapturedAmount?.ToString("F2"),
                        EShopOrderId = o.Id,
                        EShopOrderStatus = o.Status.ToString(),
                        Matched = false,
                        Note = "eShop order not found in PayPal transaction report (reporting lag expected)"
                    })
                    .ToList();

                return Results.Ok(new ReconciliationResponse
                {
                    From = from,
                    To = to,
                    Transactions = rows,
                    UnmatchedPayPalTransactions = unmatchedPayPal,
                    UnmatchedEShopOrders = unmatchedEShop,
                    TotalPayPalTransactions = transactions.Count,
                    TotalEShopOrders = allOrders.Count
                });
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public Task<IResult> HandleAsync(ReconciliationRequest request, IRepository<EShopOrder> dep)
        => Task.FromResult(Results.StatusCode(501));
}

public class ReconciliationRequest : BaseRequest { }

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse() : base(Guid.NewGuid()) { }
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public List<ReconciliationRow> Transactions { get; set; } = new();
    public List<ReconciliationRow> UnmatchedPayPalTransactions { get; set; } = new();
    public List<ReconciliationRow> UnmatchedEShopOrders { get; set; } = new();
    public int TotalPayPalTransactions { get; set; }
    public int TotalEShopOrders { get; set; }
}

public class ReconciliationRow
{
    public string? PayPalTransactionId { get; set; }
    public string? Amount { get; set; }
    public string? Currency { get; set; }
    public string? Fee { get; set; }
    public string? Status { get; set; }
    public string? Date { get; set; }
    public int? EShopOrderId { get; set; }
    public string? EShopOrderStatus { get; set; }
    public bool Matched { get; set; }
    public string? Note { get; set; }
}
