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

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderApiRequest, IOrderCheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, PayOrderApiRequest? request, IOrderCheckoutService checkout, ClaimsPrincipal user) =>
            {
                request ??= new PayOrderApiRequest();
                request.OrderId = orderId;
                request.BuyerId = BuyerIdentity.GetRequiredBuyerId(user);
                return await HandleAsync(request, checkout);
            })
            .Produces<OrderDetailsDto>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderApiRequest request, IOrderCheckoutService checkout)
    {
        var result = await checkout.PayAsync(request.BuyerId, request.OrderId, new PayOrderCommand
        {
            PaymentMethodId = request.PaymentMethodId,
            Card = request.Card == null
                ? null
                : new CardPaymentCommand
                {
                    Number = request.Card.Number ?? string.Empty,
                    Expiry = request.Card.Expiry ?? string.Empty,
                    SecurityCode = request.Card.SecurityCode,
                    Name = request.Card.Name,
                    BillingAddress = request.Card.BillingAddress == null
                        ? null
                        : new BillingAddressCommand
                        {
                            AddressLine1 = request.Card.BillingAddress.AddressLine1,
                            AddressLine2 = request.Card.BillingAddress.AddressLine2,
                            AdminArea1 = request.Card.BillingAddress.AdminArea1,
                            AdminArea2 = request.Card.BillingAddress.AdminArea2,
                            PostalCode = request.Card.BillingAddress.PostalCode,
                            CountryCode = request.Card.BillingAddress.CountryCode
                        }
                }
        });

        return Results.Ok(result);
    }
}

public class PayOrderApiRequest
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public int? PaymentMethodId { get; set; }
    public CardPaymentRequest? Card { get; set; }
}

public class CardPaymentRequest
{
    public string? Number { get; set; }
    public string? Expiry { get; set; }
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public BillingAddressRequest? BillingAddress { get; set; }
}

public class BillingAddressRequest
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
}
