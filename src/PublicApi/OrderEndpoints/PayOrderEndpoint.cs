using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public sealed class PayOrderRequest
{
    /// <summary>Id of one of the caller's saved cards. Send this or Card, not both.</summary>
    public int? PaymentMethodId { get; set; }

    /// <summary>One-off card details. Send this or PaymentMethodId, not both.</summary>
    public PaymentCardDto? Card { get; set; }
}

/// <summary>
/// Authorizes (holds) the order total. No money is captured until the order is fulfilled.
/// </summary>
public sealed class PayOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, HttpContext httpContext,
                IPaymentService paymentService, CancellationToken cancellationToken) =>
            {
                return await PaymentEndpointHelpers.ExecuteAsync(async () =>
                {
                    var buyerId = PaymentEndpointHelpers.GetBuyerId(httpContext);
                    var order = await paymentService.PayOrderAsync(buyerId, orderId,
                        request.Card?.ToPaymentCardDetails(), request.PaymentMethodId, cancellationToken);

                    return Results.Ok(order);
                });
            })
            .Produces<OrderInfo>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status402PaymentRequired)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }
}
