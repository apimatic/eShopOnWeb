using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class PayOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderCommand request, HttpContext httpContext, IPaymentService paymentService, CancellationToken cancellationToken) =>
            {
                var buyerId = PaymentEndpointHelpers.GetBuyerId(httpContext);
                var result = await paymentService.PayOrderAsync(buyerId, orderId, request, cancellationToken);

                return Results.Ok(new PaymentResponse
                {
                    OrderId = result.OrderId,
                    PaymentStatus = result.PaymentStatus,
                    AuthorizationId = result.AuthorizationId,
                    AuthorizationState = result.AuthorizationState,
                    PaymentInstrumentId = result.PaymentInstrumentId,
                    Amount = result.Amount,
                    Currency = result.Currency,
                    IdempotentReplay = result.IdempotentReplay
                });
            })
            .Produces<PaymentResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status402PaymentRequired)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }
}
