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

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderApiRequest, ICheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, PayOrderApiRequest request, ICheckoutService checkout, ClaimsPrincipal user) =>
            {
                request.OrderId = orderId;
                request.BuyerId = ApiUser.GetBuyerId(user);
                return await HandleAsync(request, checkout);
            })
            .Produces<OrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderApiRequest request, ICheckoutService checkout)
    {
        var (order, payment) = await checkout.PayAsync(request.BuyerId!, request.OrderId, new PayOrderRequest
        {
            PaymentMethodId = request.PaymentMethodId,
            Card = request.Card is null ? null : new CardPaymentDetails
            {
                Name = request.Card.Name,
                Number = request.Card.Number,
                Expiry = request.Card.Expiry,
                SecurityCode = request.Card.SecurityCode,
                BillingAddress = request.Card.BillingAddress is null ? null : new CardBillingAddress
                {
                    AddressLine1 = request.Card.BillingAddress.AddressLine1,
                    AddressLine2 = request.Card.BillingAddress.AddressLine2,
                    AdminArea2 = request.Card.BillingAddress.AdminArea2,
                    AdminArea1 = request.Card.BillingAddress.AdminArea1,
                    PostalCode = request.Card.BillingAddress.PostalCode,
                    CountryCode = request.Card.BillingAddress.CountryCode
                }
            }
        });

        return Results.Ok(PaymentResponseMapper.Map(order, payment));
    }
}

public class PayOrderApiRequest : BaseRequest
{
    public int OrderId { get; set; }
    public string? BuyerId { get; set; }
    public int? PaymentMethodId { get; set; }
    public PayCardRequest? Card { get; set; }
}

public class PayCardRequest
{
    public string? Name { get; set; }
    public string? Number { get; set; }
    public string? Expiry { get; set; }
    public string? SecurityCode { get; set; }
    public PayCardAddressRequest? BillingAddress { get; set; }
}

public class PayCardAddressRequest
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
}
