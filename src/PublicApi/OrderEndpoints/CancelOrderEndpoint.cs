using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentApi;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: cancels the order before fulfilment and releases the held funds, so no money
/// ever moved. Restricted to the administrator role.
/// </summary>
public class CancelOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                       AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                IOrderPaymentService orderPaymentService,
                IPaymentGateway gateway,
                CancellationToken cancellationToken) =>
            {
                var order = await orderPaymentService.CancelAsync(orderId, cancellationToken);
                var response = new OrderStateResponse
                {
                    OrderId = order.Id,
                    Order = PaymentViewMapper.ToView(order, gateway.Currency)
                };
                return Results.Ok(response);
            })
            .Produces<OrderStateResponse>()
            .WithTags("OrderEndpoints");
    }
}
