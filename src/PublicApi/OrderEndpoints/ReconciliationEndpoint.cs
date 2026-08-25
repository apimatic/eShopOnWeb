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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IPayPalService>
{
    private readonly IRepository<Order> _orderRepo;

    public ReconciliationEndpoint(IRepository<Order> orderRepo) => _orderRepo = orderRepo;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (string from, string to, IPayPalService paypal) =>
            {
                return await HandleAsync(new ReconciliationRequest { From = from, To = to }, paypal);
            })
            .Produces<ReconciliationResponse>()
            .Produces(400)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IPayPalService paypal)
    {
        if (string.IsNullOrEmpty(request.From) || string.IsNullOrEmpty(request.To))
            return Results.BadRequest(new { error = "from and to query parameters are required." });

        try
        {
            var transactions = await paypal.GetTransactionsAsync(request.From, request.To, CancellationToken.None);

            // Load orders in DB that have a PayPal authorization or capture
            var allOrders = await _orderRepo.ListAsync(new CustomerOrdersWithPaymentSpec(""));
            // Note: above spec filters by buyerId="" which won't match anything.
            // Use a spec without buyer filter for reconciliation.

            var allOrdersSpec = new AllOrdersWithPaymentSpec();
            var orders = await _orderRepo.ListAsync(allOrdersSpec);

            var txById = transactions.ToDictionary(t => t.TransactionId ?? "", t => t);
            var ordersByCapture = orders
                .Where(o => !string.IsNullOrEmpty(o.PayPalCaptureId))
                .ToDictionary(o => o.PayPalCaptureId!, o => o);

            var rows = new List<ReconciliationRow>();

            // PayPal transactions
            foreach (var tx in transactions)
            {
                if (tx.TransactionId == null) continue;
                // Try to match to an order by capture or PayPal order reference
                Order? matchedOrder = null;
                if (tx.PayPalReferenceId != null)
                    ordersByCapture.TryGetValue(tx.PayPalReferenceId, out matchedOrder);

                rows.Add(new ReconciliationRow
                {
                    TransactionId = tx.TransactionId,
                    Status = tx.Status,
                    Amount = tx.Amount,
                    InitiationDate = tx.InitiationDate,
                    InvoiceId = tx.InvoiceId,
                    MatchedOrderId = matchedOrder?.Id,
                    MatchStatus = matchedOrder != null ? "Matched" : "PayPalOnly"
                });
            }

            // Orders in eShop that have no matching PayPal transaction in the range
            foreach (var order in orders.Where(o => !string.IsNullOrEmpty(o.PayPalCaptureId)))
            {
                bool alreadyListed = rows.Any(r => r.MatchedOrderId == order.Id);
                if (!alreadyListed)
                {
                    rows.Add(new ReconciliationRow
                    {
                        TransactionId = order.PayPalCaptureId,
                        MatchedOrderId = order.Id,
                        Amount = order.CapturedAmount,
                        MatchStatus = "eShopOnly"
                    });
                }
            }

            return Results.Ok(new ReconciliationResponse(request.CorrelationId())
            {
                From = request.From,
                To = request.To,
                TotalPayPalTransactions = transactions.Count,
                Rows = rows
            });
        }
        catch (PayPalException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}

public class ReconciliationRequest : BaseRequest
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }
    public string? From { get; set; }
    public string? To { get; set; }
    public int TotalPayPalTransactions { get; set; }
    public List<ReconciliationRow> Rows { get; set; } = new();
}

public class ReconciliationRow
{
    public string? TransactionId { get; set; }
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public string? InitiationDate { get; set; }
    public string? InvoiceId { get; set; }
    public int? MatchedOrderId { get; set; }
    public string? MatchStatus { get; set; }
}
