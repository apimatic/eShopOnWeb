using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Authorizes (holds) the order total for the signed-in shopper, paying with one-off card details or one
/// of the shopper's saved cards. Does not take the money. Idempotent: a repeat returns the existing hold.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, HttpContext context) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, context);
            })
            .Produces<PayOrderResponse>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, HttpContext context)
    {
        var response = new PayOrderResponse(request.CorrelationId());
        var paymentService = context.RequestServices.GetRequiredService<IPaymentService>();

        var card = request.Card?.ToCardDetails();
        var payment = await paymentService.AuthorizeAsync(request.OrderId, context.User.BuyerId(), card, request.PaymentMethodId);

        response.OrderId = payment.OrderId;
        response.Status = payment.Status.ToString();
        response.AuthorizationId = payment.AuthorizationId;
        response.AuthorizationStatus = payment.AuthorizationStatus;
        response.AmountHeld = payment.Amount;
        response.Currency = payment.CurrencyCode;
        response.PaymentMethod = payment.PaymentMethodDescription;

        return Results.Ok(response);
    }
}

public class PayOrderRequest : BaseRequest
{
    public int OrderId { get; set; }

    /// <summary>One of the caller's saved cards to pay with. Mutually exclusive with <see cref="Card"/>.</summary>
    public int? PaymentMethodId { get; set; }

    /// <summary>One-off card details. Mutually exclusive with <see cref="PaymentMethodId"/>.</summary>
    public CardModel? Card { get; set; }
}

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(Guid correlationId) : base(correlationId) { }
    public PayOrderResponse() { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public decimal AmountHeld { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? PaymentMethod { get; set; }
}
