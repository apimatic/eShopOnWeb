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

public class CancelOrderRequest
{
    public int OrderId { get; set; }
}

/// <summary>
/// POST /api/orders/{orderId}/cancel — operator cancels an order before fulfilment; the held funds
/// are released so no money ever moved. Admin only.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, IOrderPaymentService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService service, ClaimsPrincipal user) =>
                await HandleAsync(new CancelOrderRequest { OrderId = orderId }, service, user))
            .Produces<OrderDto>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request, IOrderPaymentService service, ClaimsPrincipal user)
    {
        var order = await service.CancelAsync(request.OrderId);
        return Results.Ok(order.ToDto());
    }
}
