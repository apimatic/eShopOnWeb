using System.Security.Claims;
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

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, HttpContext httpContext, IOrderPaymentService orderPaymentService) =>
            {
                request.OrderId = orderId;
                request.BuyerId = httpContext.User.Identity?.Name
                    ?? httpContext.User.FindFirstValue(ClaimTypes.Name)
                    ?? string.Empty;
                return await HandleAsync(request, orderPaymentService);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService orderPaymentService)
    {
        CardPaymentDetails? card = null;
        if (request.Card is not null)
        {
            card = new CardPaymentDetails
            {
                Number = request.Card.Number ?? string.Empty,
                Expiry = request.Card.Expiry ?? string.Empty,
                SecurityCode = request.Card.SecurityCode ?? string.Empty,
                Name = request.Card.Name,
                BillingAddress = request.Card.BillingAddress is null
                    ? null
                    : new CardBillingAddress
                    {
                        AddressLine1 = request.Card.BillingAddress.Street ?? request.Card.BillingAddress.AddressLine1,
                        AddressLine2 = request.Card.BillingAddress.AddressLine2,
                        AdminArea2 = request.Card.BillingAddress.City ?? request.Card.BillingAddress.AdminArea2,
                        AdminArea1 = request.Card.BillingAddress.State ?? request.Card.BillingAddress.AdminArea1,
                        PostalCode = request.Card.BillingAddress.ZipCode ?? request.Card.BillingAddress.PostalCode,
                        CountryCode = request.Card.BillingAddress.Country ?? request.Card.BillingAddress.CountryCode ?? "US"
                    }
            };
        }

        var order = await orderPaymentService.PayAsync(request.OrderId, request.BuyerId, card, request.PaymentMethodId);
        return Results.Ok(new PayOrderResponse
        {
            OrderId = order.Id,
            Order = OrderDto.From(order)
        });
    }
}

public class PayOrderRequest : BaseRequest
{
    public CardRequest? Card { get; set; }
    public int? PaymentMethodId { get; set; }
    internal int OrderId { get; set; }
    internal string BuyerId { get; set; } = string.Empty;
}

public class PayOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public OrderDto Order { get; set; } = new();
}
