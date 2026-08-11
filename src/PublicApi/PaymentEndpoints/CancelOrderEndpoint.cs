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
/// Operator action: cancels an order before fulfilment, releasing the held funds so no money moved.
/// Restricted to administrators. POST /api/orders/{orderId}/cancel
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, int>
{
    private readonly IOrderPaymentService _service;

    public CancelOrderEndpoint(IOrderPaymentService service) => _service = service;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId) => await HandleAsync(orderId))
            .Produces<CancelOrderResponse>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId)
    {
        var payment = await _service.CancelAsync(orderId);
        return Results.Ok(new CancelOrderResponse { OrderId = orderId, PaymentStatus = payment.Status.ToString() });
    }
}
