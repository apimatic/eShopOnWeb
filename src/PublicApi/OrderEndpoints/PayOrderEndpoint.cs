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

public class PayOrderRequest : BaseRequest
{
    [System.Text.Json.Serialization.JsonIgnore]
    public int OrderId { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;

    public int? PaymentMethodId { get; set; }
    public CardDetailsRequest? Card { get; set; }
}

public class CardDetailsRequest
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public BillingAddressRequest? BillingAddress { get; set; }
}

public class BillingAddressRequest
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
    public int OrderId { get; set; }
    public OrderDto Order { get; set; } = new();
}

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, PayOrderRequest request, IOrderPaymentService service, ClaimsPrincipal user) =>
            {
                request.OrderId = orderId;
                request.BuyerId = user.RequireBuyerId();
                return await HandleAsync(request, service);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService service)
    {
        CardPaymentDetails? card = null;
        if (request.Card is not null)
        {
            card = new CardPaymentDetails
            {
                Number = request.Card.Number,
                Expiry = request.Card.Expiry,
                SecurityCode = request.Card.SecurityCode,
                Name = request.Card.Name,
                BillingAddress = request.Card.BillingAddress is null ? null : new BillingAddressDetails
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

        var order = await service.PayAsync(request.OrderId, request.BuyerId, new PayOrderCommand
        {
            PaymentMethodId = request.PaymentMethodId,
            Card = card
        });

        return Results.Ok(new PayOrderResponse
        {
            OrderId = order.Id,
            Order = PaymentResponseMapper.ToDto(order)
        });
    }
}
