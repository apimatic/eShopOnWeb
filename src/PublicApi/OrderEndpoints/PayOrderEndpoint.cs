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

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, ICheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, HttpContext http, ICheckoutService checkout) =>
            {
                request.OrderId = orderId;
                request.BuyerId = EndpointUser.RequireBuyerId(http);
                return await HandleAsync(request, checkout);
            })
            .Produces<PayOrderResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, ICheckoutService checkout)
    {
        CardDetails? card = null;
        if (request.Card is not null)
        {
            var billing = request.Card.BillingAddress ?? new CardBillingAddressRequest();
            card = new CardDetails(
                request.Card.Number ?? string.Empty,
                request.Card.Expiry ?? string.Empty,
                request.Card.SecurityCode ?? string.Empty,
                request.Card.Name ?? string.Empty,
                new CardBillingAddress(
                    billing.AddressLine1 ?? string.Empty,
                    billing.AdminArea2,
                    billing.AdminArea1,
                    billing.PostalCode ?? string.Empty,
                    billing.CountryCode ?? string.Empty));
        }

        var order = await checkout.PayAsync(request.BuyerId!, request.OrderId, card, request.PaymentMethodId);
        return Results.Ok(new PayOrderResponse
        {
            OrderId = order.Id,
            Order = OrderDtoMapper.ToDto(order)
        });
    }
}

public class PayOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
    public string? BuyerId { get; set; }
    public int? PaymentMethodId { get; set; }
    public CardPaymentRequest? Card { get; set; }
}

public class CardPaymentRequest
{
    public string? Number { get; set; }
    public string? Expiry { get; set; }
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public CardBillingAddressRequest? BillingAddress { get; set; }
}

public class CardBillingAddressRequest
{
    public string? AddressLine1 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
}

public class PayOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public OrderDto Order { get; set; } = new();
}
