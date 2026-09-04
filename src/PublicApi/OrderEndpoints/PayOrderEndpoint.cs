using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Authorizes the order total with PayPal (a hold). Either card details for a one-off
/// payment, or one of the shopper's saved cards, are used. Idempotent in effect.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, int, PayOrderRequest>
{
    private readonly IPaymentService _paymentService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public PayOrderEndpoint(IPaymentService paymentService, IHttpContextAccessor httpContextAccessor)
    {
        _paymentService = paymentService;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request) =>
            {
                return await HandleAsync(orderId, request);
            })
            .Produces<PayOrderResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, PayOrderRequest request)
    {
        var user = _httpContextAccessor.HttpContext?.User;
        var buyerId = user?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(buyerId))
            return Results.Unauthorized();

        var result = await _paymentService.AuthorizeOrderAsync(orderId, buyerId, ToCardDetails(request?.Card), request?.PaymentMethodId);

        var response = new PayOrderResponse(request?.CorrelationId() ?? Guid.NewGuid())
        {
            OrderId = result.OrderId,
            OrderStatus = result.OrderStatus,
            PaymentStatus = result.PaymentStatus,
            Amount = result.Amount,
            Currency = result.Currency,
            AuthorizationId = result.AuthorizationId,
            PaymentSourceDescription = result.PaymentSourceDescription
        };

        return Results.Ok(response);
    }

    private static CardDetails? ToCardDetails(CardPaymentRequest? card)
    {
        if (card is null) return null;

        return new CardDetails
        {
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            Name = card.Name,
            BillingAddress = card.BillingAddress is null ? null : new CardBillingAddress
            {
                AddressLine1 = card.BillingAddress.AddressLine1,
                AddressLine2 = card.BillingAddress.AddressLine2,
                AdminArea1 = card.BillingAddress.AdminArea1,
                AdminArea2 = card.BillingAddress.AdminArea2,
                PostalCode = card.BillingAddress.PostalCode,
                CountryCode = card.BillingAddress.CountryCode
            }
        };
    }
}