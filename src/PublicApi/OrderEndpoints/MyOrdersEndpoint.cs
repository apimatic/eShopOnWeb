using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.Payment;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class MyOrdersEndpoint : IEndpoint<IResult, MyOrdersRequest, IRepository<Order>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext ctx,
                   IRepository<Order> orderRepo,
                   IRepository<PaymentRecord> paymentRepo) =>
            {
                var buyerId = ctx.User.Identity?.Name ?? string.Empty;
                var request = new MyOrdersRequest { BuyerId = buyerId };
                return await HandleAsync(request, orderRepo, paymentRepo);
            })
            .Produces<List<MyOrderDto>>(200)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(MyOrdersRequest request, IRepository<Order> repo)
        => throw new System.NotSupportedException();

    private static async Task<IResult> HandleAsync(
        MyOrdersRequest request,
        IRepository<Order> orderRepo,
        IRepository<PaymentRecord> paymentRepo)
    {
        if (string.IsNullOrEmpty(request.BuyerId)) return Results.Unauthorized();

        var ordersSpec = new CustomerOrdersWithItemsSpecification(request.BuyerId);
        var orders = await orderRepo.ListAsync(ordersSpec);

        var result = new List<MyOrderDto>();
        foreach (var order in orders)
        {
            var prSpec = new PaymentRecordByOrderIdSpec(order.Id);
            var paymentRecord = (await paymentRepo.ListAsync(prSpec)).FirstOrDefault();

            result.Add(new MyOrderDto
            {
                OrderId = order.Id,
                OrderDate = order.OrderDate,
                Total = order.Total(),
                PaymentStatus = order.PaymentStatus.ToString(),
                PayPalAuthorizationId = paymentRecord?.AuthorizationId,
                PayPalCaptureId = paymentRecord?.CaptureId,
                CapturedAmount = paymentRecord?.CapturedAmount,
                TotalRefunded = paymentRecord?.TotalRefundedAmount,
                Items = order.OrderItems.Select(i => new OrderItemDto
                {
                    Name = i.ItemOrdered.ProductName,
                    UnitPrice = i.UnitPrice,
                    Quantity = i.Units
                }).ToList()
            });
        }

        return Results.Ok(result);
    }
}
