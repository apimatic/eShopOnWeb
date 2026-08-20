using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class FulfilOrderEndpoint : IEndpoint<IResult, int, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IOrderPaymentService service) => await HandleAsync(orderId, service))
            .Produces<OrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IOrderPaymentService service)
    {
        var order = await service.FulfilAsync(orderId);
        return Results.Ok(PaymentEndpointHelpers.ToResponse(order));
    }
}
