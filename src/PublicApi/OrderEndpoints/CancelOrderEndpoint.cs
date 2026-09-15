using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: cancel an order before fulfilment, releasing the shopper's held funds so no
/// money ever moved. Restricted to the administrator role.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, OrderActionRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IOrderPaymentService service) => await HandleAsync(new OrderActionRequest(orderId), service))
            .Produces<OrderPaymentResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderActionRequest request, IOrderPaymentService service)
    {
        var order = await service.CancelAsync(request.OrderId);
        return Results.Ok(PaymentApiMapper.ToResponse(order));
    }
}
