using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderCheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, PayOrderRequest request, HttpContext httpContext, IOrderCheckoutService checkout) =>
            {
                return await HandleAsync(orderId, request, httpContext, checkout);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(PayOrderRequest request, IOrderCheckoutService checkout) =>
        throw new System.NotSupportedException();

    private async Task<IResult> HandleAsync(int orderId, PayOrderRequest request, HttpContext httpContext, IOrderCheckoutService checkout)
    {
        var buyerId = CreateOrderEndpoint.BuyerId(httpContext);
        var hasCard = request.Card != null && !string.IsNullOrWhiteSpace(request.Card.Number);
        var hasSaved = request.PaymentMethodId.HasValue;

        if (hasCard == hasSaved)
        {
            throw new CheckoutException(400, "Pay with either card details or a saved paymentMethodId, not both or neither.");
        }

        var order = hasSaved
            ? await checkout.PayWithSavedCardAsync(orderId, buyerId, request.PaymentMethodId!.Value)
            : await checkout.PayWithCardAsync(orderId, buyerId, MapCard(request.Card!));

        var response = new PayOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Order = OrderResponseMapper.Map(order)
        };
        return Results.Ok(response);
    }

    internal static CardPaymentSource MapCard(PayCardRequest card) => new()
    {
        Number = card.Number,
        Expiry = card.Expiry,
        SecurityCode = card.SecurityCode,
        Name = card.Name,
        BillingAddress = card.BillingAddress == null
            ? null
            : new CardBillingAddress
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
