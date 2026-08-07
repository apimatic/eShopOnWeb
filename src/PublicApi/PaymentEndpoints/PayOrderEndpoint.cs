using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints.Models;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Pays an awaiting-payment order with PayPal, using either one-off card details or a saved card.
/// Idempotent: paying an already-paid order returns its current state without charging again.
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
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, PayOrderRequest request, IOrderPaymentService orderPaymentService) =>
            {
                request ??= new PayOrderRequest();
                request.OrderId = orderId;
                return await HandleAsync(request, orderPaymentService);
            })
            .Produces<OrderDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status402PaymentRequired)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService orderPaymentService)
    {
        var buyerId = _httpContextAccessor.HttpContext?.User.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var instruction = new PaymentInstruction
        {
            Card = request.Card?.ToCardDetails(),
            SavedPaymentMethodId = request.SavedPaymentMethodId
        };

        if (!instruction.IsValid)
        {
            return Results.BadRequest(new { message = "Provide exactly one payment source: either 'card' details or a 'savedPaymentMethodId'." });
        }

        try
        {
            var order = await orderPaymentService.PayOrderAsync(buyerId, request.OrderId, instruction);
            return Results.Ok(OrderDto.FromOrder(order));
        }
        catch (OrderNotFoundException ex)
        {
            return Results.NotFound(new { message = ex.Message });
        }
        catch (PaymentMethodNotFoundException ex)
        {
            return Results.NotFound(new { message = ex.Message });
        }
        catch (PaymentFailedException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status402PaymentRequired, title: "Payment failed");
        }
    }
}

/// <summary>Request body for paying an order. Supply exactly one of <see cref="Card"/> or <see cref="SavedPaymentMethodId"/>.</summary>
public class PayOrderRequest
{
    /// <summary>Route-bound order id; not part of the request body.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int OrderId { get; set; }

    /// <summary>One-off card details for this payment.</summary>
    public CardModel? Card { get; set; }

    /// <summary>Id of one of the shopper's saved cards to pay with instead.</summary>
    public int? SavedPaymentMethodId { get; set; }
}
