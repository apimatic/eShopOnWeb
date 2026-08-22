using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, IOrderPaymentService payments, HttpContext http) =>
            {
                request.OrderId = orderId;
                request.BuyerId = http.User.RequireBuyerId();
                return await HandleAsync(request, payments);
            })
            .Produces<OrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService payments)
    {
        if (request.PaymentMethodId.HasValue && request.Card != null)
            throw new ArgumentException("Provide either card details or a saved paymentMethodId, not both.");
        if (!request.PaymentMethodId.HasValue && request.Card == null)
            throw new ArgumentException("Provide card details or a saved paymentMethodId.");

        var order = request.PaymentMethodId.HasValue
            ? await payments.PayWithSavedCardAsync(request.BuyerId!, request.OrderId, request.PaymentMethodId.Value, default)
            : await payments.PayWithCardAsync(request.BuyerId!, request.OrderId, CardInputMapper.Map(request.Card!), default);

        return Results.Ok(OrderResponse.From(order));
    }
}

public class PayOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
    public string? BuyerId { get; set; }
    public int? PaymentMethodId { get; set; }
    public CardDetailsRequest? Card { get; set; }
}
