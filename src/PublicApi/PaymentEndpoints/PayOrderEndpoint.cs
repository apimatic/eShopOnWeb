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

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class PayOrderRequest : BaseRequest
{
    /// <summary>Card details for a one-off payment. Provide this OR <see cref="PaymentMethodId"/>, not both.</summary>
    public CardInput? Card { get; set; }

    /// <summary>A saved card of the shopper's to pay with instead. Provide this OR <see cref="Card"/>.</summary>
    public int? PaymentMethodId { get; set; }

    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
}

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(Guid correlationId) : base(correlationId) { }
    public PayOrderResponse() { }

    public int OrderId { get; set; }
    public OrderView Order { get; set; } = new();
}

/// <summary>
/// Authorizes an order's total (places a hold, does not take the money) using either raw card
/// details or one of the shopper's saved cards. Idempotent: a double-click returns the existing hold.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, IOrderPaymentService service) =>
            {
                request.OrderId = orderId;
                request.BuyerId = user.GetBuyerId();
                return await HandleAsync(request, service);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService service)
    {
        var order = await service.AuthorizeAsync(
            request.OrderId, request.BuyerId,
            request.Card?.ToPayPalCard(), request.PaymentMethodId);

        var response = new PayOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Order = OrderView.From(order)
        };
        return Results.Ok(response);
    }
}
