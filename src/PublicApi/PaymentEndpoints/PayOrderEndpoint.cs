using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Authorizes (holds) the order total. The request either carries card details for a one-off
/// payment or names one of the shopper's saved cards. The hold equals the order total to the cent.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, IPaymentService paymentService) =>
            {
                request.OrderId = orderId;
                request.BuyerId = PaymentMapping.GetBuyerId(user);
                return await HandleAsync(request, paymentService);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IPaymentService paymentService)
    {
        var response = new PayOrderResponse(request.CorrelationId());

        var instruction = new PayInstruction(
            request.Card?.ToGatewayCard(),
            request.PaymentMethodId,
            request.SaveCard);

        var view = await paymentService.AuthorizeOrderAsync(request.BuyerId, request.OrderId, instruction);
        response.Order = view.ToDto();
        return Results.Ok(response);
    }
}

public class PayOrderRequest : BaseRequest
{
    public CardRequestDto? Card { get; set; }
    public int? PaymentMethodId { get; set; }
    public bool SaveCard { get; set; }

    internal int OrderId { get; set; }
    internal string BuyerId { get; set; } = string.Empty;
}

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(Guid correlationId) : base(correlationId) { }
    public PayOrderResponse() { }

    public OrderPaymentDto Order { get; set; } = new();
}
