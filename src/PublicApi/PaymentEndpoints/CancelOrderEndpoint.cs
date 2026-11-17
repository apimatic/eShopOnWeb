using System.Threading;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class CancelOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService service, CancellationToken cancellationToken) =>
            {
                var order = await service.CancelAsync(orderId, cancellationToken);
                var response = new CancelOrderResponse(System.Guid.NewGuid())
                {
                    OrderId = order.Id,
                    Order = PaymentDtoMapper.Map(order)
                };

                return Results.Ok(response);
            })
            .Produces<CancelOrderResponse>()
            .WithTags("OrderPaymentEndpoints");
    }
}
