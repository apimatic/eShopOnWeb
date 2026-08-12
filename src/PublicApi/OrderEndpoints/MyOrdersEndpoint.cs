using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class MyOrdersEndpoint : IEndpoint<IResult, IRepository<Order>, IRepository<PaymentReference>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext, IRepository<Order> orderRepo, IRepository<PaymentReference> paymentRepo) =>
            {
                var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId))
                    return Results.Unauthorized();

                return await HandleAsync(orderRepo, paymentRepo, userId);
            })
            .Produces<List<MyOrderDto>>()
            .WithName("MyOrders")
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(IRepository<Order> orderRepo, IRepository<PaymentReference> paymentRepo, string userId)
    {
        var orders = await orderRepo.ListAsync(o => o.BuyerId == userId);
        var paymentRefs = (await paymentRepo.ListAllAsync()).ToList();

        var result = orders.Select(o =>
        {
            var payment = paymentRefs.FirstOrDefault(p => p.OrderId == o.Id);
            return new MyOrderDto
            {
                OrderId = o.Id,
                Total = o.Total(),
                PaymentState = payment?.State.ToString() ?? PaymentState.AwaitingPayment.ToString(),
                CreatedAt = o.OrderDate
            };
        }).ToList();

        return Results.Ok(result);
    }
}

public record MyOrderDto
{
    public int OrderId { get; set; }
    public decimal Total { get; set; }
    public string PaymentState { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
