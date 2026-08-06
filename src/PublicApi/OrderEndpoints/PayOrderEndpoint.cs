using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Pays for an order with PayPal, using either one-off card details or one of the shopper's saved cards.
/// Idempotent: a repeated request never produces a second charge.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public PayOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, IOrderPaymentService paymentService) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, paymentService);
            })
            .Produces<PayOrderResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService paymentService)
    {
        var buyerId = BuyerIdAccessor.GetBuyerId(_httpContextAccessor.HttpContext?.User);
        if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

        var card = request.Card?.ToCardDetails();

        Order? order;
        try
        {
            order = await paymentService.PayAsync(buyerId, request.OrderId, card, request.SavedPaymentMethodId);
        }
        catch (PaymentMethodNotFoundException ex)
        {
            return Results.NotFound(new { message = ex.Message });
        }
        catch (PaymentGatewayException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
        catch (System.ArgumentException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
        catch (System.InvalidOperationException ex)
        {
            return Results.Conflict(new { message = ex.Message });
        }

        if (order is null) return Results.NotFound(new { message = $"Order {request.OrderId} was not found." });

        return Results.Ok(new PayOrderResponse(request.CorrelationId()) { Order = order.ToDto() });
    }
}
