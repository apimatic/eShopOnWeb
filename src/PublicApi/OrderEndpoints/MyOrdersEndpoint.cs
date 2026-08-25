using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class MyOrdersEndpoint : IEndpoint<IResult, MyOrdersRequest, IRepository<Order>>
{
    private readonly IRepository<Payment> _paymentRepository;

    public MyOrdersEndpoint(IRepository<Payment> paymentRepository)
    {
        _paymentRepository = paymentRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IRepository<Order> orderRepository, ClaimsPrincipal user) =>
            {
                var buyerId = user.FindFirst(ClaimTypes.Name)?.Value ?? "";
                return await HandleAsync(new MyOrdersRequest { BuyerId = buyerId }, orderRepository);
            })
            .Produces<MyOrdersResponse>(200)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(MyOrdersRequest request, IRepository<Order> orderRepository)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
            return Results.Unauthorized();

        var spec = new CustomerOrdersWithItemsSpecification(request.BuyerId);
        var orders = await orderRepository.ListAsync(spec);

        var items = new List<MyOrderDto>();
        foreach (var order in orders)
        {
            var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(order.Id));
            items.Add(new MyOrderDto
            {
                OrderId = order.Id,
                OrderDate = order.OrderDate,
                Status = order.Status.ToString(),
                Total = order.Total(),
                AuthorizationId = payment?.AuthorizationId,
                CaptureId = payment?.CaptureId,
                CapturedAmount = payment?.CapturedAmount,
                PayPalFee = payment?.PayPalFee,
                NetAmount = payment?.NetAmount,
                Currency = payment?.Currency,
                TotalRefunded = payment?.TotalRefunded(),
                Items = order.OrderItems.Select(i => new OrderItemDto
                {
                    ProductName = i.ItemOrdered.ProductName,
                    UnitPrice = i.UnitPrice,
                    Quantity = i.Units
                }).ToList()
            });
        }

        return Results.Ok(new MyOrdersResponse { Orders = items });
    }
}
