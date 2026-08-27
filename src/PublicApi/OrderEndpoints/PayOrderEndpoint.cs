using System;
using System.Security.Claims;
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
/// Authorizes (holds) the order total with PayPal, using either one-off card details
/// or one of the caller's saved cards. No money moves yet.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, ClaimsPrincipal, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, IOrderPaymentService orderPaymentService) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, user, orderPaymentService);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, ClaimsPrincipal user, IOrderPaymentService orderPaymentService)
    {
        var response = new PayOrderResponse(request.CorrelationId());

        var payment = await orderPaymentService.PayOrderAsync(
            user.GetBuyerId(),
            request.OrderId,
            request.Card?.ToGatewayCard(),
            request.PaymentMethodId);

        response.OrderId = request.OrderId;
        response.Payment = payment.ToDto();
        return Results.Ok(response);
    }
}

public class PayOrderRequest : BaseRequest
{
    public int OrderId { get; set; }

    /// <summary>One-off card details. Mutually exclusive with PaymentMethodId.</summary>
    public CardDetailsDto? Card { get; set; }

    /// <summary>Id of one of the caller's saved cards. Mutually exclusive with Card.</summary>
    public int? PaymentMethodId { get; set; }
}

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(Guid correlationId) : base(correlationId) { }
    public PayOrderResponse() { }

    public int OrderId { get; set; }
    public PaymentDto? Payment { get; set; }
}
