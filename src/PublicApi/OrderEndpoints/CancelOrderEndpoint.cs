using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public record CancelOrderResponse(int OrderId, string Status);

public class CancelOrderEndpoint : IEndpoint<IResult, int, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IPaymentService svc) => await HandleAsync(orderId, svc))
            .Produces<CancelOrderResponse>()
            .WithTags("Orders");
    }

    public async Task<IResult> HandleAsync(int orderId, IPaymentService svc)
    {
        await svc.CancelOrderAsync(orderId);
        return Results.Ok(new CancelOrderResponse(orderId, "Cancelled"));
    }
}
