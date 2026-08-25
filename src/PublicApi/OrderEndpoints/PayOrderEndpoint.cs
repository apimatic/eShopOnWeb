using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Authorizes (holds) the order total - either with a one-off card or a previously saved card.
/// Does not take the money; that happens at fulfilment.
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
                request.BuyerId = user.Identity!.Name!;
                return await HandleAsync(request, paymentService);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IPaymentService paymentService)
    {
        var response = new PayOrderResponse(request.CorrelationId());

        var card = request.Card is null ? null : MapCard(request.Card);

        var payment = await paymentService.AuthorizeOrderAsync(request.OrderId, request.BuyerId, card, request.SavedPaymentMethodId, CancellationToken.None);

        response.OrderId = request.OrderId;
        response.PayPalOrderId = payment.PayPalOrderId ?? string.Empty;
        response.PayPalAuthorizationId = payment.PayPalAuthorizationId ?? string.Empty;
        response.AuthorizationStatus = payment.AuthorizationStatus ?? payment.Status.ToString();
        response.AuthorizationExpiresAt = payment.AuthorizationExpiresAt;

        return Results.Ok(response);
    }

    private static CardDetails MapCard(CardDetailsRequest card) => new()
    {
        Number = card.Number,
        Expiry = card.Expiry,
        SecurityCode = card.SecurityCode,
        CardholderName = card.CardholderName,
        BillingAddress = new BillingAddress
        {
            AddressLine1 = card.BillingAddress.AddressLine1,
            AddressLine2 = card.BillingAddress.AddressLine2,
            City = card.BillingAddress.City,
            State = card.BillingAddress.State,
            PostalCode = card.BillingAddress.PostalCode,
            CountryCode = card.BillingAddress.CountryCode
        }
    };
}
