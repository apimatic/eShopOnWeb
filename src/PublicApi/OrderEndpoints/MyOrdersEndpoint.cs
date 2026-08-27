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

/// <summary>
/// Lists the caller's orders with their payment state.
/// </summary>
public class MyOrdersEndpoint : IEndpoint<IResult, ClaimsPrincipal, IRepository<Order>, IRepository<Payment>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IRepository<Order> orderRepository, IRepository<Payment> paymentRepository) =>
            {
                return await HandleAsync(user, orderRepository, paymentRepository);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, IRepository<Order> orderRepository,
        IRepository<Payment> paymentRepository)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var orders = await orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId));
        var payments = await paymentRepository.ListAsync(new PaymentsByBuyerSpecification(buyerId));

        var response = new ListMyOrdersResponse
        {
            Orders = orders.Select(o => new OrderDto
            {
                OrderId = o.Id,
                OrderDate = o.OrderDate,
                Status = o.Status.ToString(),
                Total = o.Total(),
                Payment = payments.FirstOrDefault(p => p.OrderId == o.Id) is { } payment
                    ? PaymentDto.FromPayment(payment)
                    : null
            }).ToList()
        };
        return Results.Ok(response);
    }
}
