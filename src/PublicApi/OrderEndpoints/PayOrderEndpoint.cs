using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequestBody
{
    public CardDto? Card { get; set; }
    public int? PaymentMethodId { get; set; }
}

public class PayOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public CardDto? Card { get; set; }
    public int? PaymentMethodId { get; set; }
}

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(Guid correlationId) : base(correlationId) { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public decimal? AuthorizedAmount { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }
}

/// <summary>
/// Authorizes (holds, does not capture) the order total by card or a saved payment method.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequestBody body, HttpContext httpContext, IOrderPaymentService paymentService) =>
            {
                var request = new PayOrderRequest
                {
                    OrderId = orderId,
                    BuyerId = httpContext.User.Identity!.Name!,
                    Card = body.Card,
                    PaymentMethodId = body.PaymentMethodId
                };
                return await HandleAsync(request, paymentService);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService paymentService)
    {
        var response = new PayOrderResponse(request.CorrelationId());

        var card = request.Card is null ? null : new PayPalCardDetails
        {
            Number = request.Card.Number,
            CardholderName = request.Card.CardholderName,
            ExpiryMonth = request.Card.ExpiryMonth,
            ExpiryYear = request.Card.ExpiryYear,
            SecurityCode = request.Card.SecurityCode,
            BillingAddress = request.Card.BillingAddress is null ? null : new PayPalBillingAddress
            {
                AddressLine1 = request.Card.BillingAddress.AddressLine1,
                AddressLine2 = request.Card.BillingAddress.AddressLine2,
                AdminArea1 = request.Card.BillingAddress.AdminArea1,
                AdminArea2 = request.Card.BillingAddress.AdminArea2,
                PostalCode = request.Card.BillingAddress.PostalCode,
                CountryCode = request.Card.BillingAddress.CountryCode
            }
        };

        var order = await paymentService.AuthorizePaymentAsync(request.OrderId, request.BuyerId, card, request.PaymentMethodId);

        response.OrderId = order.Id;
        response.Status = order.Status.ToString();
        response.AuthorizationId = order.Payment?.PayPalAuthorizationId;
        response.AuthorizationStatus = order.Payment?.AuthorizationStatus;
        response.AuthorizedAmount = order.Payment?.AuthorizedAmount;
        response.Currency = order.Payment?.CurrencyCode;
        response.AuthorizationExpiresAt = order.Payment?.AuthorizationExpiresAt;
        return Results.Ok(response);
    }
}
