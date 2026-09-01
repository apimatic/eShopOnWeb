using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator: cancel the order before fulfilment. The shopper's held funds are released
/// (the PayPal authorization is voided); no money ever moves.
/// </summary>
public class CancelOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IPaymentService paymentService, CancellationToken cancellationToken) =>
            {
                var payment = await paymentService.CancelOrderAsync(orderId, cancellationToken);

                var response = new CancelOrderResponse
                {
                    OrderId = orderId,
                    Payment = PaymentDto.FromPayment(payment)
                };
                return Results.Ok(response);
            })
            .Produces<CancelOrderResponse>()
            .WithTags("OrderEndpoints");
    }
}

public class CancelOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public PaymentDto? Payment { get; set; }
}
