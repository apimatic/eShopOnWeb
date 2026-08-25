using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderBody
{
    // Provide exactly one of Card or PaymentMethodId.
    public CardDetailsDto? Card { get; set; }
    public int? PaymentMethodId { get; set; }
}

public class PayOrderRequest : BaseRequest
{
    public PayOrderRequest(int orderId, string buyerId, PayOrderBody body)
    {
        OrderId = orderId;
        BuyerId = buyerId;
        Body = body;
    }

    public int OrderId { get; }
    public string BuyerId { get; }
    public PayOrderBody Body { get; }
}

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(Guid correlationId) : base(correlationId) { }

    public int OrderId { get; set; }
    public PaymentDto Payment { get; set; } = new();
}

/// <summary>
/// Authorizes (holds) an order's total with a one-off card or a saved card. Does not take the
/// money - that happens at fulfilment.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderBody body, ClaimsPrincipal user, IOrderPaymentService paymentService) =>
            {
                var request = new PayOrderRequest(orderId, user.Identity!.Name!, body);
                return await HandleAsync(request, paymentService);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService paymentService)
    {
        if ((request.Body.Card is null) == (request.Body.PaymentMethodId is null))
        {
            return Results.BadRequest("Provide either card details or a paymentMethodId, not both or neither.");
        }

        var response = new PayOrderResponse(request.CorrelationId()) { OrderId = request.OrderId };

        try
        {
            var card = request.Body.Card?.ToCardDetails();
            var payment = await paymentService.AuthorizePaymentAsync(request.OrderId, request.BuyerId, card, request.Body.PaymentMethodId);
            response.Payment = PaymentDto.FromEntity(payment);
            return Results.Ok(response);
        }
        catch (Exception ex) when (ex is OrderNotFoundException or PaymentMethodNotFoundException or InvalidOrderStateException
            or PayerActionRequiredException or PaymentDeclinedException or PaymentGatewayException)
        {
            return PaymentExceptionResults.Map(ex);
        }
    }
}
