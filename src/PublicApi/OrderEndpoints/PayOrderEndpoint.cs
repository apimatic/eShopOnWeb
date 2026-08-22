using System.Security.Claims;
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

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, IOrderPaymentService payments, ClaimsPrincipal user) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                request.OrderId = orderId;
                request.BuyerId = buyerId;
                return await HandleAsync(request, payments);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService payments)
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
                BillingAddress = request.Card.BillingAddress is null ? null : new CardBillingAddress
                {
                    AddressLine1 = request.Card.BillingAddress.AddressLine1,
                    AddressLine2 = request.Card.BillingAddress.AddressLine2,
                    AdminArea2 = request.Card.BillingAddress.AdminArea2,
                    AdminArea1 = request.Card.BillingAddress.AdminArea1,
                    PostalCode = request.Card.BillingAddress.PostalCode,
                    CountryCode = request.Card.BillingAddress.CountryCode ?? "US"
                }
            };
        }

        var order = await payments.PayAsync(
            request.OrderId,
            request.BuyerId!,
            request.PaymentMethodId,
            card,
            default);

        return Results.Ok(new PayOrderResponse
        {
            OrderId = order.Id,
            Order = OrderPaymentDto.From(order)
        });
    }
}

public class PayOrderRequest : BaseRequest
{
    public int? PaymentMethodId { get; set; }
    public PayCardRequest? Card { get; set; }
    internal int OrderId { get; set; }
    internal string? BuyerId { get; set; }
}

public class PayCardRequest
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string? Name { get; set; }
    public PayBillingAddressRequest? BillingAddress { get; set; }
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

public class PayOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public OrderPaymentDto Order { get; set; } = new();
}
