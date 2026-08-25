using System.Security.Claims;
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

public class MyOrdersEndpoint : IEndpoint<IResult, IRepository<Order>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext ctx,
                   IRepository<Order> orderRepo,
                   IRepository<OrderPayment> paymentRepo) =>
            {
                var username = ctx.User.FindFirstValue(ClaimTypes.Name);
                if (string.IsNullOrEmpty(username)) return Results.Unauthorized();

                var orders = await orderRepo.ListAsync(new CustomerOrdersWithItemsSpecification(username));
                var orderIds = orders.Select(o => o.Id).ToList();

                var payments = new List<OrderPayment>();
                foreach (var id in orderIds)
                {
                    var p = await paymentRepo.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpec(id));
                    if (p != null) payments.Add(p);
                }

                var response = orders.Select(o =>
                {
                    var pay = payments.FirstOrDefault(p => p.OrderId == o.Id);
                    return new MyOrderDto
                    {
                        OrderId = o.Id,
                        OrderDate = o.OrderDate,
                        Total = o.Total(),
                        PaymentStatus = pay?.Status.ToString() ?? "PendingPayment",
                        Items = o.OrderItems.Select(i => new OrderItemDto
                        {
                            ProductName = i.ItemOrdered.ProductName,
                            UnitPrice = i.UnitPrice,
                            Quantity = i.Units
                        }).ToList()
                    };
                });

                return Results.Ok(new { Orders = response });
            })
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(IRepository<Order> repository)
        => throw new NotImplementedException();
}

public class MyOrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string PaymentStatus { get; set; } = "";
    public List<OrderItemDto> Items { get; set; } = new();
}

public class OrderItemDto
{
    public string ProductName { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}
