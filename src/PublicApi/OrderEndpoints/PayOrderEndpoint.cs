using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Authorizes (holds) the order total using either one-off card details or a saved card.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, IPaymentService paymentService) =>
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
        if (string.IsNullOrWhiteSpace(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        var command = new PayOrderCommand
        {
            PaymentMethodId = request.PaymentMethodId,
            Card = request.Card == null
                ? null
                : new CardCommand
                {
                    Number = request.Card.Number,
                    ExpirationMonth = request.Card.ExpirationMonth,
                    ExpirationYear = request.Card.ExpirationYear,
                    SecurityCode = request.Card.SecurityCode,
                    Name = request.Card.Name,
                    Address = new ShippingAddressCommand
                    {
                        Street = request.Card.Address?.Street,
                        City = request.Card.Address?.City,
                        State = request.Card.Address?.State,
                        Country = request.Card.Address?.Country,
                        ZipCode = request.Card.Address?.ZipCode
                    }
                }
        };

        var summary = await paymentService.AuthorizeOrderAsync(request.BuyerId, request.OrderId, command);
        var payment = summary.Payment;

        return Results.Ok(new PayOrderResponse
        {
            OrderId = summary.OrderId,
            PaymentId = payment?.PaymentId,
            Status = payment?.Status ?? summary.Status,
            AuthorizationId = payment?.AuthorizationId,
            AuthorizationState = payment?.AuthorizationState,
            ExpiresAt = payment?.AuthorizationExpiresAt,
            AmountCents = payment?.AmountCents ?? summary.TotalCents,
            Currency = payment?.Currency ?? summary.Currency,
            Order = summary
        });
    }
}
