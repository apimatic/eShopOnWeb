using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class FulfilOrderRequest
{
    public int OrderId { get; set; }
}

/// <summary>
/// POST /api/orders/{orderId}/fulfil — operator marks the order fulfilled; the held funds are captured
/// (money taken now). A stale hold is renewed first. Admin only.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest, IOrderPaymentService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService service, ClaimsPrincipal user) =>
                await HandleAsync(new FulfilOrderRequest { OrderId = orderId }, service, user))
            .Produces<OrderDto>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(FulfilOrderRequest request, IOrderPaymentService service, ClaimsPrincipal user)
    {
        var order = await service.FulfilAsync(request.OrderId);
        return Results.Ok(order.ToDto());
    }
}
