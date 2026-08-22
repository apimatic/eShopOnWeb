using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderCheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, PayOrderRequest? request, ClaimsPrincipal user, IOrderCheckoutService checkout) =>
                await HandleAsync(orderId, request ?? new PayOrderRequest(), user, checkout))
            .Produces<OrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(PayOrderRequest request, IOrderCheckoutService checkout)
        => Task.FromResult(Results.BadRequest());

    private static async Task<IResult> HandleAsync(int orderId, PayOrderRequest request, ClaimsPrincipal user, IOrderCheckoutService checkout)
    {
        var buyerId = CheckoutHttp.BuyerId(user);
        CardPaymentInput? card = request.Card is null
            ? null
            : new CardPaymentInput
            {
                Number = request.Card.Number ?? string.Empty,
                Expiry = request.Card.Expiry ?? string.Empty,
                SecurityCode = request.Card.SecurityCode,
                Name = request.Card.Name,
                BillingAddress = request.Card.BillingAddress is null
                    ? null
                    : new CardBillingAddress
                    {
                        AddressLine1 = request.Card.BillingAddress.AddressLine1,
                        AddressLine2 = request.Card.BillingAddress.AddressLine2,
                        AdminArea1 = request.Card.BillingAddress.AdminArea1,
                        AdminArea2 = request.Card.BillingAddress.AdminArea2,
                        PostalCode = request.Card.BillingAddress.PostalCode,
                        CountryCode = request.Card.BillingAddress.CountryCode
                    }
            };

        var order = await checkout.PayAsync(buyerId, orderId, card, request.PaymentMethodId);
        return Results.Ok(CheckoutHttp.ToResponse(order));
    }
}

public class PayOrderRequest
{
    public int? PaymentMethodId { get; set; }
    public PayCardRequest? Card { get; set; }
}

public class PayCardRequest
{
    public string? Number { get; set; }
    public string? Expiry { get; set; }
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public PayBillingAddressRequest? BillingAddress { get; set; }
}

public class PayBillingAddressRequest
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
}
