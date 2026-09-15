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
/// Operator action: cancels an order before fulfilment, releasing the shopper's held funds so no money moves.
/// Restricted to the administrator role. Idempotent.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, OrderIdRequest>
{
    private readonly IPaymentService _payments;

    public CancelOrderEndpoint(IPaymentService payments)
    {
        _payments = payments;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId) => await HandleAsync(new OrderIdRequest(orderId)))
            .Produces<PaymentActionResponse>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderIdRequest request)
    {
        var payment = await _payments.CancelAsync(request.OrderId);
        return Results.Ok(new PaymentActionResponse(payment.ToDto()));
    }
}
