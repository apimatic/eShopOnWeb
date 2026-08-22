using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class FulfilOrderEndpoint : IEndpoint<IResult, int, ICheckoutPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, ICheckoutPaymentService checkout) =>
            {
                var order = await checkout.FulfilAsync(orderId);
                return Results.Ok(new OrderActionResponse
                {
                    OrderId = order.Id,
                    Order = OrderEndpointHelpers.ToDto(order)
                });
            })
            .Produces<OrderActionResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(int request, ICheckoutPaymentService checkout) =>
        Task.FromResult(Results.BadRequest());
}
