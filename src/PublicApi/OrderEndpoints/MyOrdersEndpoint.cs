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

public class MyOrdersEndpoint : IEndpoint<IResult, MyOrdersRequest, IReadRepository<Order>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext httpContext,
                   IReadRepository<Order> orderRepo,
                   IReadRepository<OrderPayment> paymentRepo) =>
            {
                var userName = httpContext.User.Identity!.Name!;
                var ordersSpec = new CustomerOrdersWithItemsSpecification(userName);
                var orders = await orderRepo.ListAsync(ordersSpec);

                var result = new List<MyOrderDto>();
                foreach (var order in orders)
                {
                    var paySpec = new OrderPaymentByOrderIdSpec(order.Id);
                    var payment = await paymentRepo.FirstOrDefaultAsync(paySpec);
                    result.Add(new MyOrderDto(
                        order.Id,
                        order.OrderDate,
                        order.Total(),
                        payment?.Status.ToString() ?? "NoPayment",
                        payment?.AuthorizationId,
                        payment?.CaptureId,
                        order.OrderItems.Select(i => new OrderItemDto(
                            i.ItemOrdered.ProductName,
                            i.Units,
                            i.UnitPrice)).ToList()
                    ));
                }
                return Results.Ok(result);
            })
            .Produces<List<MyOrderDto>>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(MyOrdersRequest request, IReadRepository<Order> service)
        => Task.FromResult(Results.StatusCode(501));
}

public class MyOrdersRequest : BaseRequest { }

public record OrderItemDto(string ProductName, int Quantity, decimal UnitPrice);

public record MyOrderDto(
    int OrderId,
    System.DateTimeOffset OrderDate,
    decimal Total,
    string PaymentStatus,
    string? AuthorizationId,
    string? CaptureId,
    List<OrderItemDto> Items);
