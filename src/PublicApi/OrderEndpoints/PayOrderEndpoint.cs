using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Integrations.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Authorizes the exact order total on PayPal (a hold; no money taken yet).
/// The request carries one-off card details OR names one of the shopper's saved cards.
/// Idempotent: repeating it on an already-authorized order returns the same hold -
/// a double-click never authorizes the shopper twice.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, string, IPaymentProcessingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest body, ClaimsPrincipal principal, IPaymentProcessingService payments) =>
            {
                return await HandleAsync(new PayOrderRequest(orderId, body), principal.Identity?.Name ?? string.Empty, payments);
            })
            .Produces<PayOrderResponse>()
            .Produces<PayOrderResponse>(StatusCodes.Status402PaymentRequired)
            .Produces<PayOrderResponse>(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, string buyerId, IPaymentProcessingService payments)
    {
        var response = new PayOrderResponse(request.CorrelationId());

        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        CardDetails? card = request.Card?.ToCardDetails();
        string? savedCardId = request.PaymentMethodId?.ToString();

        var order = await payments.PayOrderAsync(buyerId, request.OrderId, card, savedCardId);

        response.OrderId = order.Id;
        response.Status = order.Status.ToString();
        response.Payment = order.Payment is null ? null : PaymentStateDto.From(order.Payment);
        return Results.Ok(response);
    }
}

public class PayOrderRequest : BaseRequest
{
    /// <summary>Route value, merged in by the handler.</summary>
    public int OrderId { get; init; }

    /// <summary>One-off card details. Mutually exclusive with PaymentMethodId.</summary>
    public CardRequestDto? Card { get; init; }

    /// <summary>Identifier of one of the caller's saved cards. Mutually exclusive with Card.</summary>
    public int? PaymentMethodId { get; init; }

    public PayOrderRequest() { }

    public PayOrderRequest(int orderId, PayOrderRequest source)
    {
        OrderId = orderId;
        Card = source.Card;
        PaymentMethodId = source.PaymentMethodId;
        _correlationId = source.CorrelationId();
    }
}

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(Guid correlationId) : base(correlationId) { }
    public PayOrderResponse() { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public PaymentStateDto? Payment { get; set; }
}
