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
/// Operator action: fulfils the order, capturing the held funds. A stale hold is renewed before
/// capture rather than failing outright; a hold that can no longer be renewed is reported in terms
/// an operator can act on. Restricted to the administrator role.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                       AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                IOrderPaymentService orderPaymentService,
                IPaymentGateway gateway,
                CancellationToken cancellationToken) =>
            {
                var order = await orderPaymentService.FulfilAsync(orderId, cancellationToken);
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

/// <summary>The common shape for endpoints that report an order's state after acting on it.</summary>
public class OrderStateResponse
{
    public int OrderId { get; set; }
    public OrderView Order { get; set; } = new();
}
