using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class OrderPaymentResponse
{
    public int OrderId { get; set; }
    public OrderPaymentDto Payment { get; set; } = new();
}

/// <summary>
/// POST /api/orders/{orderId}/fulfil — operator marks the order fulfilled; the money is captured here.
/// A stale authorization is renewed rather than failing the fulfilment outright. Administrator only.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, int, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IPaymentService paymentService) =>
                await HandleAsync(orderId, paymentService))
            .Produces<OrderPaymentResponse>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IPaymentService paymentService)
    {
        var payment = await paymentService.FulfilAsync(orderId);
        return Results.Ok(new OrderPaymentResponse { OrderId = payment.OrderId, Payment = PaymentMapper.ToDto(payment) });
    }
}
