using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payment;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, ICheckoutPaymentService>
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
            (int orderId, PayOrderRequest request, ICheckoutPaymentService checkout) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, checkout);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, ICheckoutPaymentService checkout)
    {
        var buyerId = _httpContextAccessor.HttpContext?.User.Identity?.Name ?? string.Empty;
        CardPaymentInput? card = request.Card is null
            ? null
            : new CardPaymentInput(
                request.Card.Number,
                request.Card.Expiry,
                request.Card.SecurityCode,
                request.Card.Name,
                request.Card.BillingAddress is null
                    ? null
                    : new CardBillingAddress(
                        request.Card.BillingAddress.AddressLine1,
                        request.Card.BillingAddress.AddressLine2,
                        request.Card.BillingAddress.AdminArea2,
                        request.Card.BillingAddress.AdminArea1,
                        request.Card.BillingAddress.PostalCode,
                        request.Card.BillingAddress.CountryCode));

        var order = await checkout.PayAsync(request.OrderId, buyerId, card, request.PaymentMethodId, default);
        return Results.Ok(new PayOrderResponse
        {
            OrderId = order.Id,
            Order = OrderResponseMapper.Map(order)
        });
    }
}
