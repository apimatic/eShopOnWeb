using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// POST /api/orders/{orderId}/fulfil — operator action. Marks the order fulfilled and captures
/// the held funds, re-authorizing first if the hold has gone stale.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IOrderPaymentService service) =>
            {
                var order = await service.FulfilAsync(orderId);
                return Results.Ok(order.ToDto());
            })
            .Produces<OrderDto>()
            .WithTags("OrderPaymentEndpoints");
    }
}
