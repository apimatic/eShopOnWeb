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

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService>
{
    private readonly IPayPalGateway _payPal;

    public PayOrderEndpoint(IPayPalGateway payPal)
    {
        _payPal = payPal;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, PayOrderRequest request, ClaimsPrincipal user, IOrderPaymentService payments) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, user, payments);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService payments)
        => HandleAsync(request, new ClaimsPrincipal(), payments);

    public async Task<IResult> HandleAsync(PayOrderRequest request, ClaimsPrincipal user, IOrderPaymentService payments)
    {
        var buyerId = CallerIdentity.GetBuyerId(user);
        CardPaymentRequest? card = null;
        if (request.Card is not null)
        {
            card = new CardPaymentRequest
            {
                Number = request.Card.Number ?? string.Empty,
                Expiry = request.Card.Expiry ?? string.Empty,
                SecurityCode = request.Card.SecurityCode,
                Name = request.Card.Name,
                BillingAddress = request.Card.BillingAddress is null
                    ? null
                    : new BillingAddressRequest
                    {
                        AddressLine1 = request.Card.BillingAddress.AddressLine1,
                        AddressLine2 = request.Card.BillingAddress.AddressLine2,
                        AdminArea2 = request.Card.BillingAddress.AdminArea2,
                        AdminArea1 = request.Card.BillingAddress.AdminArea1,
                        PostalCode = request.Card.BillingAddress.PostalCode,
                        CountryCode = request.Card.BillingAddress.CountryCode
                    }
            };
        }

        var order = await payments.AuthorizePaymentAsync(request.OrderId, buyerId, card, request.PaymentMethodId);
        return Results.Ok(new PayOrderResponse
        {
            Order = OrderDto.From(order, _payPal.Currency)
        });
    }
}

public class PayOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
    public int? PaymentMethodId { get; set; }
    public CardDetailsRequest? Card { get; set; }
}

public class CardDetailsRequest
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
    public string? AddressLine2 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
}

public class PayOrderResponse : BaseResponse
{
    public OrderDto Order { get; set; } = new();
}
