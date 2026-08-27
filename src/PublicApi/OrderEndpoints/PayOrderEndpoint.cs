using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Authorizes the order total: puts a hold on the money without taking it.
/// Pays either with one-off card details or with one of the caller's saved cards.
/// </summary>
public class PayOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, IOrderPaymentService paymentService) =>
            {
                return await HandleAsync(request, user, orderId, paymentService);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, ClaimsPrincipal user, int orderId,
        IOrderPaymentService paymentService)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        CardDetails? card = null;
        if (request.SavedCardId is null)
        {
            if (request.Card is null || string.IsNullOrWhiteSpace(request.Card.Number)
                || string.IsNullOrWhiteSpace(request.Card.Expiry))
            {
                return Results.BadRequest("Provide either card details (number and expiry) or a savedCardId.");
            }

            card = new CardDetails
            {
                Number = request.Card.Number,
                Expiry = request.Card.Expiry,
                SecurityCode = request.Card.SecurityCode,
                Name = request.Card.Name,
                AddressLine1 = request.Card.AddressLine1,
                AdminArea2 = request.Card.City,
                AdminArea1 = request.Card.State,
                PostalCode = request.Card.PostalCode,
                CountryCode = request.Card.CountryCode
            };
        }

        var payment = await paymentService.AuthorizePaymentAsync(buyerId, orderId, card, request.SavedCardId);

        var response = new PayOrderResponse(request.CorrelationId())
        {
            OrderId = orderId,
            OrderStatus = "PaymentAuthorized",
            Payment = PaymentDto.FromPayment(payment)
        };
        return Results.Ok(response);
    }
}
