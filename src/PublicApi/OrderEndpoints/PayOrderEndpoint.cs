using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, ICheckoutService>
{
    private readonly PayPalSettings _payPalSettings;

    public PayOrderEndpoint(PayPalSettings payPalSettings)
    {
        _payPalSettings = payPalSettings;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, PayOrderRequest request, ICheckoutService checkout, ClaimsPrincipal user) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrWhiteSpace(buyerId))
                {
                    return Results.Unauthorized();
                }

                CardPayment? card = null;
                if (request.Card is not null)
                {
                    var billing = request.Card.BillingAddress is null
                        ? null
                        : new CardBillingAddress(
                            request.Card.BillingAddress.AddressLine1,
                            request.Card.BillingAddress.AddressLine2,
                            request.Card.BillingAddress.AdminArea2,
                            request.Card.BillingAddress.AdminArea1,
                            request.Card.BillingAddress.PostalCode,
                            request.Card.BillingAddress.CountryCode);
                    card = new CardPayment(
                        request.Card.Number ?? string.Empty,
                        request.Card.Expiry ?? string.Empty,
                        request.Card.SecurityCode ?? string.Empty,
                        request.Card.Name ?? string.Empty,
                        billing);
                }

                var order = await checkout.PayAsync(buyerId, orderId, card, request.PaymentMethodId);
                return Results.Ok(OrderDtoMapper.From(order, _payPalSettings.Currency));
            })
            .Produces<OrderDto>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(PayOrderRequest request, ICheckoutService checkout) =>
        throw new System.NotSupportedException("Use the route handler.");
}

public class PayOrderRequest
{
    public PayCardRequest? Card { get; set; }
    public int? PaymentMethodId { get; set; }
}

public class PayCardRequest
{
    public string? Number { get; set; }
    public string? Expiry { get; set; }
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public PayBillingAddressRequest? BillingAddress { get; set; }

    public override string ToString() => "[card redacted]";
}

public class PayBillingAddressRequest
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
}
