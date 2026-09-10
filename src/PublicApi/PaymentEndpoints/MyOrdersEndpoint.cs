using System.Collections.Generic;
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

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class MyOrdersResponse
{
    public List<OrderDto> Orders { get; set; } = new();
}

/// <summary>Returns the caller's own orders with their payment state.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, ClaimsPrincipal, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IOrderPaymentService service) => await HandleAsync(user, service))
            .Produces<MyOrdersResponse>()
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, IOrderPaymentService service)
    {
        var orders = await service.GetMyOrdersAsync(user.GetBuyerId());
        return Results.Ok(new MyOrdersResponse { Orders = orders.Select(OrderDto.From).ToList() });
    }
}
