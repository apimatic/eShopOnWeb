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

namespace Microsoft.eShopWeb.PublicApi.OrderPaymentEndpoints;

public class MyOrdersRequest
{
    public string BuyerId { get; set; } = string.Empty;
}

public class MyOrdersResponse
{
    public List<OrderPaymentResponse> Orders { get; set; } = new();
}

/// <summary>Returns the caller's own orders with their payment state.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, MyOrdersRequest, IPaymentProcessingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IPaymentProcessingService service) =>
            {
                var buyerId = user.GetBuyerId();
                if (buyerId is null) return Results.Unauthorized();
                return await HandleAsync(new MyOrdersRequest { BuyerId = buyerId }, service);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(MyOrdersRequest request, IPaymentProcessingService service)
    {
        var orders = await service.GetOrdersForBuyerAsync(request.BuyerId);
        return Results.Ok(new MyOrdersResponse
        {
            Orders = orders.Select(OrderPaymentMapping.ToResponse).ToList()
        });
    }
}
