using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// POST /api/orders/{orderId}/fulfil — operator marks the order fulfilled; the held funds are
/// captured (money taken). A stale authorization is renewed before capture. Administrator only.
/// </summary>
public class FulfilOrderEndpoint : PaymentEndpointBase, IEndpoint<IResult, OrderActionRequest, IPaymentService>
{
    public FulfilOrderEndpoint(IHttpContextAccessor httpContextAccessor) : base(httpContextAccessor) { }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IPaymentService service) => await HandleAsync(new OrderActionRequest(orderId), service))
            .Produces<OrderPaymentView>()
            .WithTags("PaymentOrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderActionRequest request, IPaymentService service)
    {
        var view = await service.FulfilAsync(request.OrderId, RequestAborted);
        return Results.Ok(view);
    }
}
