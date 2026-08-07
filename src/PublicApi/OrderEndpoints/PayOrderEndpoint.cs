using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentShared;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Pays for the signed-in shopper's order with PayPal, using card details or a saved card.
/// Idempotent: paying an already-paid order returns the existing result without charging again.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, IOrderPaymentService orderPaymentService) =>
            {
                request.OrderId = orderId;
                request.BuyerId = user.GetBuyerId();
                return await HandleAsync(request, orderPaymentService);
            })
            .Produces<PayOrderResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints")
            .WithMetadata(new SwaggerOperationAttribute("Pays for an order with PayPal", "Pays with one-off card details or a saved card."));
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService orderPaymentService)
    {
        var response = new PayOrderResponse(request.CorrelationId());

        if (request.Card is null && request.SavedPaymentMethodId is null)
        {
            throw new PaymentProcessingException("Provide either card details or a saved payment method id.");
        }

        if (request.Card is not null && request.SavedPaymentMethodId is not null)
        {
            throw new PaymentProcessingException("Provide either card details or a saved payment method id, not both.");
        }

        var order = await orderPaymentService.PayOrderAsync(
            request.BuyerId,
            request.OrderId,
            request.Card?.ToCardDetails(),
            request.SavedPaymentMethodId);

        response.OrderId = order.Id;
        response.PaymentStatus = order.PaymentStatus.ToString();
        response.Order = OrderDto.FromOrder(order);

        return Results.Ok(response);
    }
}
