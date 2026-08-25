using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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

public class ReconciliationEndpoint : IEndpoint
{
    private readonly IReadRepository<Order> _orderRepo;
    private readonly IPayPalGateway _paypal;

    public ReconciliationEndpoint(IReadRepository<Order> orderRepo, IPayPalGateway paypal)
    {
        _orderRepo = orderRepo;
        _paypal = paypal;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                       AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (string from, string to, HttpContext ctx) =>
            {
                return await HandleAsync(from, to, ctx.RequestAborted);
            })
            .Produces<ReconciliationResponse>(200)
            .ProducesProblem(400)
            .WithTags("OrderEndpoints");
    }

    private async Task<IResult> HandleAsync(string from, string to, System.Threading.CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
            return Results.BadRequest("from and to date-time parameters are required.");

        List<TransactionRecord> transactions;
        try
        {
            var raw = await _paypal.GetTransactionsAsync(from, to, ct);
            transactions = new List<TransactionRecord>(raw.Count);
            foreach (var t in raw)
                transactions.Add(new TransactionRecord
                {
                    TransactionId = t.TransactionId,
                    ReferenceId = t.ReferenceId,
                    Status = t.Status,
                    Amount = t.Amount,
                    Fee = t.Fee,
                    InvoiceId = t.InvoiceId,
                    InitiationDate = t.InitiationDate
                });
        }
        catch (PayPalException ex)
        {
            return Results.Problem($"PayPal transaction search failed: {ex.Message}", statusCode: 502);
        }

        // Build a lookup of eShop orders by PayPal order ID
        var invoiceIds = transactions
            .Where(t => !string.IsNullOrEmpty(t.InvoiceId))
            .Select(t => t.InvoiceId!)
            .Distinct()
            .ToHashSet();

        var allOrders = await _orderRepo.ListAsync(ct);
        var orderMap = new Dictionary<string, ApplicationCore.Entities.OrderAggregate.Order>();
        foreach (var o in allOrders)
        {
            if (o.PayPalOrderId != null) orderMap[o.PayPalOrderId] = o;
            if (!string.IsNullOrEmpty(o.Id.ToString())) orderMap[o.Id.ToString()] = o;
        }

        var rows = new List<ReconciliationRow>();
        foreach (var t in transactions)
        {
            ApplicationCore.Entities.OrderAggregate.Order? matchedOrder = null;
            if (t.InvoiceId != null) orderMap.TryGetValue(t.InvoiceId, out matchedOrder);
            if (matchedOrder == null && t.ReferenceId != null) orderMap.TryGetValue(t.ReferenceId, out matchedOrder);

            rows.Add(new ReconciliationRow
            {
                TransactionId = t.TransactionId,
                ReferenceId = t.ReferenceId,
                Status = t.Status,
                Amount = t.Amount,
                Fee = t.Fee,
                InvoiceId = t.InvoiceId,
                InitiationDate = t.InitiationDate,
                EShopOrderId = matchedOrder?.Id,
                EShopPaymentStatus = matchedOrder?.PaymentStatus.ToString(),
                Matched = matchedOrder != null
            });
        }

        return Results.Ok(new ReconciliationResponse
        {
            From = from,
            To = to,
            TransactionCount = rows.Count,
            UnmatchedCount = rows.Count(r => !r.Matched),
            Rows = rows
        });
    }
}

public class ReconciliationResponse
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public int TransactionCount { get; set; }
    public int UnmatchedCount { get; set; }
    public List<ReconciliationRow> Rows { get; set; } = new();
}

public class ReconciliationRow
{
    public string TransactionId { get; set; } = string.Empty;
    public string? ReferenceId { get; set; }
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public decimal? Fee { get; set; }
    public string? InvoiceId { get; set; }
    public string? InitiationDate { get; set; }
    public int? EShopOrderId { get; set; }
    public string? EShopPaymentStatus { get; set; }
    public bool Matched { get; set; }
}

public class TransactionRecord
{
    public string TransactionId { get; set; } = string.Empty;
    public string? ReferenceId { get; set; }
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public decimal? Fee { get; set; }
    public string? InvoiceId { get; set; }
    public string? InitiationDate { get; set; }
}
