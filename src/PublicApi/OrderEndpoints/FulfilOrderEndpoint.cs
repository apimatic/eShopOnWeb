using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class FulfilOrderEndpoint : IEndpoint<IResult, int, IPaymentCheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IPaymentCheckoutService payments) =>
            {
                return await HandleAsync(orderId, payments);
            })
            .Produces<OrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IPaymentCheckoutService payments)
    {
        var order = await payments.FulfilAsync(orderId);
        return Results.Ok(OrderResponseMapper.Map(order, payments.Currency));
    }
}
