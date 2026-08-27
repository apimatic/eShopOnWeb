using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest : BaseRequest
{
    /// <summary>Full card details for a one-off payment. Mutually exclusive with PaymentMethodId.</summary>
    public CardDetailsDto? Card { get; set; }

    /// <summary>Id of one of the caller's saved cards (from POST /api/payment-methods).</summary>
    public int? PaymentMethodId { get; set; }
}

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(Guid correlationId) : base(correlationId) { }
    public PayOrderResponse() { }

    public int OrderId { get; set; }
    public string Status { get; set; } = "";
    public PaymentDto Payment { get; set; } = new();
}

/// <summary>
/// Authorizes the order total: puts a hold on the money without taking it.
/// Pay either with one-off card details or with one of the shopper's saved cards.
/// </summary>
public class PayOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, [FromBody] PayOrderRequest request, HttpContext httpContext,
                IOrderPaymentService orderPaymentService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(orderId, request, httpContext, orderPaymentService, cancellationToken);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, PayOrderRequest request, HttpContext httpContext,
        IOrderPaymentService orderPaymentService, CancellationToken cancellationToken)
    {
        var buyerId = httpContext.User.GetBuyerId();

        var payment = await orderPaymentService.PayOrderAsync(buyerId, orderId,
            request.Card?.ToModel(), request.PaymentMethodId, cancellationToken);

        var response = new PayOrderResponse(request.CorrelationId())
        {
            OrderId = orderId,
            Status = "PaymentAuthorized",
            Payment = PaymentDto.FromEntity(payment)
        };
        return Results.Ok(response);
    }
}
