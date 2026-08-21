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
/// Operator action: cancels an order before fulfilment, voiding the authorization so the shopper's
/// held funds are released — no money ever moved.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IPaymentService paymentService) =>
            {
                return await HandleAsync(new CancelOrderRequest(orderId), paymentService);
            })
            .Produces<PaymentResponse>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request, IPaymentService paymentService)
    {
        var result = await paymentService.CancelOrderAsync(request.OrderId);
        return Results.Ok(PaymentResponse.From(result));
    }
}

public class CancelOrderRequest
{
    public CancelOrderRequest(int orderId) => OrderId = orderId;
    public int OrderId { get; }
}
