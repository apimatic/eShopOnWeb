using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderCommand, HttpContext, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, HttpContext httpContext, IPaymentService paymentService) =>
            {
                var card = request.Card;
                if (card is null && !string.IsNullOrWhiteSpace(request.Number))
                {
                    card = new CardDto
                    {
                        Number = request.Number!,
                        ExpirationMonth = request.ExpirationMonth ?? 0,
                        ExpirationYear = request.ExpirationYear ?? 0,
                        Cvc = request.Cvc ?? request.Cvv ?? string.Empty,
                        Cvv = request.Cvv,
                        Name = request.Name ?? string.Empty,
                        Address = request.Address
                    };
                }

                var command = new PayOrderCommand
                {
                    OrderId = orderId,
                    PaymentMethodId = request.PaymentMethodId,
                    SavedPaymentMethodId = request.SavedPaymentMethodId,
                    SavedCardId = request.SavedCardId,
                    Card = card
                };
                return await HandleAsync(command, httpContext, paymentService);
            })
            .Produces<OrderDto>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status402PaymentRequired)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("Orders");
    }

    public async Task<IResult> HandleAsync(PayOrderCommand request, HttpContext httpContext, IPaymentService paymentService)
    {
        var buyerId = PaymentEndpointHelpers.RequireBuyer(httpContext);
        var order = await paymentService.PayOrderAsync(buyerId, request.OrderId, request, httpContext.RequestAborted);
        return Results.Ok(order);
    }
}
