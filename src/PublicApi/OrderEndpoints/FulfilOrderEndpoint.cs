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
/// Operator action: mark the order fulfilled, which is when the held funds are actually captured.
/// A stale authorization is renewed first; one that can no longer be renewed is reported so an
/// operator can act on it. Restricted to the administrator role.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, OrderActionRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IOrderPaymentService service) => await HandleAsync(new OrderActionRequest(orderId), service))
            .Produces<OrderPaymentResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderActionRequest request, IOrderPaymentService service)
    {
        var order = await service.FulfilAsync(request.OrderId);
        return Results.Ok(PaymentApiMapper.ToResponse(order));
    }
}
