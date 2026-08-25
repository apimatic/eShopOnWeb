using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IReadRepository<Order>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                       AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (string from,
                   string to,
                   IReadRepository<Order> orderRepo,
                   PayPalPaymentService paypal,
                   CancellationToken ct) =>
            {
                if (!DateTimeOffset.TryParse(from, out var fromDate))
                    return Results.BadRequest(new { error = "Invalid 'from' date. Use ISO-8601 format." });
                if (!DateTimeOffset.TryParse(to, out var toDate))
                    return Results.BadRequest(new { error = "Invalid 'to' date. Use ISO-8601 format." });

                IReadOnlyList<TransactionRecord> transactions;
                try
                {
                    transactions = await paypal.GetTransactionsAsync(fromDate, toDate, ct);
                }
                catch (PayPalException ex)
                {
                    return Results.Problem(detail: ex.Message, statusCode: ex.HttpStatusCode);
                }

                // Load our orders in the date range
                var ordersSpec = new OrdersByDateRangeSpec(fromDate, toDate);
                var dbOrders = await orderRepo.ListAsync(ordersSpec, ct);
                var dbOrdersById = dbOrders.ToDictionary(o => o.Id);

                // Build reconciliation entries for each PayPal transaction
                var entries = transactions.Select(t => new ReconciliationEntry
                {
                    PayPalTransactionId = t.TransactionId,
                    PayPalAmount = t.Amount,
                    Currency = t.Currency,
                    PayPalStatus = t.Status,
                    CustomField = t.CustomField,
                    EShopOrderId = t.EShopOrderId,
                    EShopOrderStatus = t.EShopOrderId.HasValue && dbOrdersById.TryGetValue(t.EShopOrderId.Value, out var matchedOrder)
                        ? matchedOrder.Status.ToString()
                        : null,
                    Matched = t.EShopOrderId.HasValue && dbOrdersById.ContainsKey(t.EShopOrderId.Value)
                }).ToList();

                // Find orders in DB that have no matching PayPal transaction
                var matchedOrderIds = transactions
                    .Where(t => t.EShopOrderId.HasValue)
                    .Select(t => t.EShopOrderId!.Value)
                    .ToHashSet();

                var orphanOrders = dbOrders
                    .Where(o => o.Payment?.AuthorizationId != null && !matchedOrderIds.Contains(o.Id))
                    .Select(o => new OrphanOrderEntry
                    {
                        OrderId = o.Id,
                        OrderDate = o.OrderDate,
                        Status = o.Status.ToString(),
                        Total = o.Total().ToString("F2")
                    }).ToList();

                return Results.Ok(new ReconciliationResponse
                {
                    From = fromDate,
                    To = toDate,
                    Entries = entries,
                    OrphanOrders = orphanOrders
                });
            })
            .Produces<ReconciliationResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(ReconciliationRequest request, IReadRepository<Order> service)
        => throw new System.NotSupportedException();
}

public class ReconciliationRequest : BaseRequest { }

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationEntry> Entries { get; set; } = new();
    public List<OrphanOrderEntry> OrphanOrders { get; set; } = new();
}

public class ReconciliationEntry
{
    public string? PayPalTransactionId { get; set; }
    public string? PayPalAmount { get; set; }
    public string? Currency { get; set; }
    public string? PayPalStatus { get; set; }
    public string? CustomField { get; set; }
    public int? EShopOrderId { get; set; }
    public string? EShopOrderStatus { get; set; }
    public bool Matched { get; set; }
}

public class OrphanOrderEntry
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Total { get; set; } = string.Empty;
}
