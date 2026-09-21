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
/// POST /api/orders/{orderId}/cancel — cancel before fulfilment: the held funds are released, so no
/// money ever moved. Administrator only.
/// </summary>
public class CancelOrderEndpoint : PaymentEndpointBase, IEndpoint<IResult, OrderActionRequest, IPaymentService>
{
    public CancelOrderEndpoint(IHttpContextAccessor httpContextAccessor) : base(httpContextAccessor) { }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IPaymentService service) => await HandleAsync(new OrderActionRequest(orderId), service))
            .Produces<OrderPaymentView>()
            .WithTags("PaymentOrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderActionRequest request, IPaymentService service)
    {
        var view = await service.CancelAsync(request.OrderId, RequestAborted);
        return Results.Ok(view);
    }
}
