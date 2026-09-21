using System;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PayPalEndpoints;

public class PayOrderRequest : BaseRequest
{
    /// <summary>One-off card details, OR omit and set <see cref="SavedPaymentMethodId"/>.</summary>
    public CardDto? Card { get; set; }

    /// <summary>Id of one of the caller's saved cards to pay with instead of a one-off card.</summary>
    public int? SavedPaymentMethodId { get; set; }

    [JsonIgnore] public int OrderId { get; set; }
    [JsonIgnore] public string BuyerId { get; set; } = string.Empty;
    [JsonIgnore] public CancellationToken Ct { get; set; }
}

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(Guid correlationId) : base(correlationId) { }
    public PayOrderResponse() { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? PaymentMethod { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }
}

/// <summary>
/// Authorizes (holds) the order total using card details or a saved card. Does not take the money.
/// Idempotent: a repeat never authorizes the shopper twice.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, HttpContext http, IPaymentService service, CancellationToken ct) =>
            {
                var buyerId = PaymentMapping.CurrentBuyerId(http);
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();
                request.OrderId = orderId;
                request.BuyerId = buyerId;
                request.Ct = ct;
                return await HandleAsync(request, service);
            })
            .Produces<PayOrderResponse>()
            .WithTags("PayPalOrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IPaymentService service)
    {
        var instruction = new PayInstruction(
            request.Card is null ? null : PaymentMapping.ToCardDetails(request.Card),
            request.SavedPaymentMethodId);

        var payment = await service.PayOrderAsync(request.BuyerId, request.OrderId, instruction, request.Ct);

        var response = new PayOrderResponse(request.CorrelationId())
        {
            OrderId = payment.OrderId,
            Status = payment.Status.ToString(),
            AuthorizationId = payment.AuthorizationId,
            PaymentMethod = payment.PaymentMethodDescription,
            Amount = payment.Amount,
            Currency = payment.Currency,
            AuthorizationExpiresAt = payment.AuthorizationExpiresAt
        };
        return Results.Ok(response);
    }
}
