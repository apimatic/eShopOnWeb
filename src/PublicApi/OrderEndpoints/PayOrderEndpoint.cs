using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Models;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Authorizes (holds) the order total at PayPal, either with card details for a
/// one-off payment or with one of the shopper's saved cards. The money is not
/// taken until fulfilment.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, HttpContext httpContext, IOrderPaymentService orderPaymentService) =>
            {
                request.OrderId = orderId;
                request.BuyerId = httpContext.User.Identity?.Name;
                return await HandleAsync(request, orderPaymentService);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService orderPaymentService)
    {
        var response = new PayOrderResponse(request.CorrelationId());

        var payment = await orderPaymentService.AuthorizePaymentAsync(
            request.BuyerId!, request.OrderId, request.Card?.ToPayPalCardDetails(), request.SavedPaymentMethodId);

        response.OrderId = payment.OrderId;
        response.PaymentId = payment.Id;
        response.Status = payment.Status;
        response.AuthorizationId = payment.AuthorizationId;
        response.AuthorizationStatus = payment.AuthorizationStatus;
        response.Amount = payment.AuthorizedAmount;
        response.Currency = payment.Currency;
        response.CardBrand = payment.CardBrand;
        response.CardLast4 = payment.CardLast4;

        return Results.Ok(response);
    }
}

public class PayOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
    public string? BuyerId { get; set; }
    public CardRequest? Card { get; set; }
    public int? SavedPaymentMethodId { get; set; }
}

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(Guid correlationId) : base(correlationId) { }
    public PayOrderResponse() { }

    public int OrderId { get; set; }
    public int PaymentId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? CardBrand { get; set; }
    public string? CardLast4 { get; set; }
}
