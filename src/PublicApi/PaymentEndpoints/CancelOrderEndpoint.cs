using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Operator action: cancels an order before fulfilment, releasing any held funds.</summary>
public class CancelOrderEndpoint : IEndpoint<IResult, int, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService service) =>
                await HandleAsync(orderId, service))
            .Produces<CancelOrderResponse>()
            .WithTags("PaymentEndpoints");
    }

    public Task<IResult> HandleAsync(int orderId, IOrderPaymentService service) =>
        PaymentApiHelpers.RunAsync(async () =>
        {
            await service.CancelAsync(orderId);

            var response = new CancelOrderResponse
            {
                OrderId = orderId,
                PaymentStatus = PaymentStatus.Cancelled.ToString()
            };
            return Results.Ok(response);
        });
}
