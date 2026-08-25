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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class MyOrdersEndpoint : IEndpoint<IResult, string, IRepository<Order>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IRepository<Order> orderRepo,
                   IRepository<PaymentRecord> paymentRepo,
                   HttpContext ctx,
                   CancellationToken ct) =>
            {
                var buyerId = ctx.User.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();

                var orderSpec = new OrdersByBuyerSpec(buyerId);
                var orders = await orderRepo.ListAsync(orderSpec, ct);

                var paySpec = new PaymentRecordByOrderAndBuyerSpec(0, buyerId);
                // Load all payments for this buyer (spec by buyer only)
                var allPaySpec = new PaymentRecordsByBuyerSpec(buyerId);
                var payments = await paymentRepo.ListAsync(allPaySpec, ct);
                var paymentMap = payments.ToDictionary(p => p.OrderId);

                var result = orders.Select(o =>
                {
                    paymentMap.TryGetValue(o.Id, out var pay);
                    return new MyOrderDto
                    {
                        OrderId = o.Id,
                        OrderDate = o.OrderDate,
                        Total = o.Total(),
                        Items = o.OrderItems.Select(i => new OrderItemDto
                        {
                            ProductName = i.ItemOrdered.ProductName,
                            UnitPrice = i.UnitPrice,
                            Quantity = i.Units
                        }).ToList(),
                        PaymentStatus = pay?.Status ?? PaymentStatus.PendingPayment,
                        AuthorizationId = pay?.AuthorizationId,
                        CaptureId = pay?.CaptureId,
                        CapturedAmount = pay?.CapturedAmount
                    };
                }).ToList();

                return Results.Ok(result);
            })
            .Produces<List<MyOrderDto>>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(string request, IRepository<Order> service)
        => throw new NotImplementedException();
}
