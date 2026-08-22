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

public class PayOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
    public string? BuyerId { get; set; }
    public int? PaymentMethodId { get; set; }
    public PayOrderCardRequest? Card { get; set; }
}

public class PayOrderCardRequest
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string? Name { get; set; }
    public PayOrderAddressRequest? BillingAddress { get; set; }
}

public class PayOrderAddressRequest
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = "US";
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
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, IOrderPaymentService service, HttpContext httpContext) =>
            {
                request.OrderId = orderId;
                request.BuyerId = CreateOrderEndpoint.RequireBuyerId(httpContext);
                return await HandleAsync(request, service);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService service)
    {
        CardPaymentInput? card = null;
        if (request.Card != null)
        {
            var billing = request.Card.BillingAddress;
            card = new CardPaymentInput(
                request.Card.Number,
                request.Card.Expiry,
                request.Card.SecurityCode,
                request.Card.Name,
                billing == null
                    ? new BillingAddressInput("123 Main St.", null, "Kent", "OH", "44240", "US")
                    : new BillingAddressInput(
                        billing.AddressLine1,
                        billing.AddressLine2,
                        billing.AdminArea2,
                        billing.AdminArea1,
                        billing.PostalCode,
                        string.IsNullOrWhiteSpace(billing.CountryCode) ? "US" : billing.CountryCode));
        }

        var order = await service.PayAsync(request.BuyerId!, request.OrderId, request.PaymentMethodId, card, default);
        return Results.Ok(new PayOrderResponse
        {
            OrderId = order.Id,
            Order = OrderDtoMapper.Map(order)
        });
    }
}
