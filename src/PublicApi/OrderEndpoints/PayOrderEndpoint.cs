using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Authorizes (holds) the order total, either with one-off card details or a saved card.
/// No money moves until fulfilment.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IPaymentService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public PayOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            ([FromRoute] int orderId, [FromBody] PayOrderRequest request, IPaymentService paymentService) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, paymentService);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IPaymentService paymentService)
    {
        var buyerId = _httpContextAccessor.HttpContext?.User.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }
        if (request.Card is null && request.SavedCardId is null)
        {
            return Results.BadRequest("Provide either 'card' or 'savedCardId'.");
        }

        var payment = await paymentService.AuthorizeOrderPaymentAsync(
            buyerId, request.OrderId, MapCard(request.Card), request.SavedCardId);

        var response = new PayOrderResponse(request.CorrelationId())
        {
            OrderId = request.OrderId,
            Status = "Authorized",
            Payment = PaymentDto.FromEntity(payment)
        };
        return Results.Ok(response);
    }

    internal static CardDetails? MapCard(CardDetailsRequest? card)
    {
        if (card is null)
        {
            return null;
        }
        return new CardDetails(
            card.Number,
            card.Expiry,
            card.SecurityCode,
            card.CardholderName,
            card.BillingAddress is null ? null : new CardBillingAddress(
                card.BillingAddress.AddressLine1,
                card.BillingAddress.AddressLine2,
                card.BillingAddress.City,
                card.BillingAddress.State,
                card.BillingAddress.PostalCode,
                card.BillingAddress.CountryCode));
    }
}

public class PayOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
    public CardDetailsRequest? Card { get; set; }
    public int? SavedCardId { get; set; }
}

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(Guid correlationId) : base(correlationId) { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public PaymentDto Payment { get; set; } = new();
}
