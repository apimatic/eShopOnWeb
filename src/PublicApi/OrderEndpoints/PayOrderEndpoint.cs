using System.Security.Claims;
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

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, ICheckoutPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, ICheckoutPaymentService service, CancellationToken ct) =>
            {
                var card = ToCard(request.Card);
                var order = await service.PayAsync(orderId, user.GetBuyerId(), card, request.PaymentMethodId, ct);
                return Results.Ok(OrderResponse.From(order));
            })
            .Produces<OrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(PayOrderRequest request, ICheckoutPaymentService service) =>
        Task.FromResult(Results.StatusCode(StatusCodes.Status501NotImplemented));

    internal static PayPalCardInput? ToCard(CardDetailsRequest? card)
    {
        if (card == null || string.IsNullOrWhiteSpace(card.Number))
            return null;

        PayPalBillingAddressInput? billing = null;
        if (card.BillingAddress != null && !string.IsNullOrWhiteSpace(card.BillingAddress.CountryCode))
        {
            billing = new PayPalBillingAddressInput(
                card.BillingAddress.CountryCode,
                card.BillingAddress.AddressLine1,
                card.BillingAddress.AdminArea2,
                card.BillingAddress.AdminArea1,
                card.BillingAddress.PostalCode);
        }

        return new PayPalCardInput(
            card.Name,
            card.Number.Replace(" ", string.Empty),
            card.Expiry ?? string.Empty,
            card.SecurityCode,
            billing);
    }
}
