using System;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
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
/// Authorizes the order total (a hold, not a capture) using either raw card details
/// or one of the shopper's saved cards.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, IPaymentService paymentService, ClaimsPrincipal user) =>
            {
                request.OrderId = orderId;
                request.BuyerId = user.Identity?.Name ?? string.Empty;
                return await HandleAsync(request, paymentService);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IPaymentService paymentService)
    {
        if ((request.Card is null) == !request.PaymentMethodId.HasValue)
        {
            return Results.BadRequest("Supply exactly one of 'card' or 'paymentMethodId'.");
        }

        if (request.Card is not null)
        {
            if (string.IsNullOrWhiteSpace(request.Card.Number) ||
                !Regex.IsMatch(request.Card.Expiry ?? string.Empty, @"^\d{4}-\d{2}$"))
            {
                return Results.BadRequest("Card number and expiry (YYYY-MM) are required.");
            }
        }

        var payment = await paymentService.PayOrderAsync(request.BuyerId, request.OrderId,
            request.Card?.ToCardDetails(), request.PaymentMethodId, CancellationToken.None);

        return Results.Ok(new PayOrderResponse(request.CorrelationId())
        {
            OrderId = request.OrderId,
            Payment = payment.ToDto()
        });
    }
}

public class PayOrderRequest : BaseRequest
{
    [JsonIgnore]
    public int OrderId { get; set; }

    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;

    public CardDetailsDto? Card { get; set; }
    public int? PaymentMethodId { get; set; }
}

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(Guid correlationId) : base(correlationId) { }

    public int OrderId { get; set; }
    public PaymentStateDto Payment { get; set; } = new PaymentStateDto();
}
