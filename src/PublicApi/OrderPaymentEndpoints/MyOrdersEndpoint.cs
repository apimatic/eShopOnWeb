using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderPaymentEndpoints;

/// <summary>GET /api/my-orders — the caller's own orders with their payment state.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, ClaimsPrincipal>
{
    private readonly IOrderPaymentService _service;

    public MyOrdersEndpoint(IOrderPaymentService service)
    {
        _service = service;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ClaimsPrincipal user) => await HandleAsync(user))
            .Produces<MyOrdersResponse>()
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user)
    {
        var buyerId = CallerIdentity.GetBuyerId(user);
        var orders = await _service.GetMyOrdersAsync(buyerId);

        var response = new MyOrdersResponse
        {
            Orders = orders.Select(op => new MyOrderResponse
            {
                OrderId = op.Order.Id,
                OrderDate = op.Order.OrderDate,
                Total = op.Order.Total(),
                Payment = PaymentResponseMapper.ToResponse(op.Payment),
                Items = PaymentResponseMapper.ToLines(op.Order),
            }).ToList(),
        };

        return Results.Ok(response);
    }
}
