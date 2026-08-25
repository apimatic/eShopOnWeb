using System;
using System.Linq;
using System.Threading.Tasks;
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

public class ReconciliationEndpoint : IEndpoint<IResult, string, string>
{
    private readonly IPaymentService _paymentService;
    private readonly IReadRepository<Order> _orderRepo;

    public ReconciliationEndpoint(IPaymentService paymentService, IReadRepository<Order> orderRepo)
    {
        _paymentService = paymentService;
        _orderRepo = orderRepo;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string from, string to) =>
            {
                return await HandleAsync(from, to);
            })
            .Produces<ReconciliationResponse>()
            .ProducesProblem(400)
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(string from, string to)
    {
        if (!DateTimeOffset.TryParse(from, out var fromDate))
            return Results.BadRequest("Invalid 'from' date format. Use ISO-8601.");
        if (!DateTimeOffset.TryParse(to, out var toDate))
            return Results.BadRequest("Invalid 'to' date format. Use ISO-8601.");

        var transactions = await _paymentService.SearchTransactionsAsync(from, to);
        var ordersSpec = new OrdersInDateRangeSpecification(fromDate, toDate);
        var orders = await _orderRepo.ListAsync(ordersSpec);

        // Index orders by PayPal order ID for join
        var ordersByPayPalId = orders
            .Where(o => o.PayPalOrderId != null)
            .ToDictionary(o => o.PayPalOrderId!, o => o);

        var records = transactions.Select(t =>
        {
            var matched = orders.FirstOrDefault(o =>
                o.PayPalOrderId == t.PayPalReference || o.CaptureId == t.TransactionId);
            return new ReconciliationRecord
            {
                OrderId = matched?.Id,
                OrderDate = matched?.OrderDate,
                OrderTotal = matched?.Total(),
                PayPalOrderId = matched?.PayPalOrderId,
                TransactionId = t.TransactionId,
                Amount = t.Amount,
                Fee = t.Fee,
                Status = t.Status,
                CreateTime = t.CreateTime
            };
        }).ToList();

        // Also include orders that have no PayPal transaction (unmatched orders)
        var matchedOrderIds = records.Where(r => r.OrderId.HasValue).Select(r => r.OrderId!.Value).ToHashSet();
        var unmatchedOrders = orders
            .Where(o => o.PaymentStatus != OrderPaymentStatus.Pending && !matchedOrderIds.Contains(o.Id))
            .Select(o => new ReconciliationRecord
            {
                OrderId = o.Id,
                OrderDate = o.OrderDate,
                OrderTotal = o.Total(),
                PayPalOrderId = o.PayPalOrderId
            });
        records.AddRange(unmatchedOrders);

        return Results.Ok(new ReconciliationResponse { Records = records });
    }
}
