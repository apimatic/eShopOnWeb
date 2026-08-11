using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// POST /api/orders/{orderId}/pay — authorizes (holds) the order total. The request carries either
/// card details for a one-off payment or the id of one of the shopper's saved cards. Idempotent in effect.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, IOrderPaymentService service, HttpContext http) =>
            {
                request.OrderId = orderId;
                request.BuyerId = http.BuyerId();
                return await HandleAsync(request, service);
            })
            .Produces<OrderResponse>()
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService service)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        var hasCard = request.Card is not null;
        var hasSaved = request.SavedCardId.HasValue;
        if (hasCard == hasSaved)
        {
            throw new ValidationException("Provide either card details or a saved card id (exactly one) to pay.");
        }

        var instruction = new PaymentInstruction(request.Card?.ToCardDetails(), request.SavedCardId);
        var order = await service.AuthorizeAsync(request.OrderId, request.BuyerId, instruction);

        return Results.Ok(OrderResponse.From(order));
    }
}
