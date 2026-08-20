using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IPayPalGateway _payPal;

    public PayOrderEndpoint(IHttpContextAccessor httpContextAccessor, IPayPalGateway payPal)
    {
        _httpContextAccessor = httpContextAccessor;
        _payPal = payPal;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest? request, IOrderPaymentService payments) =>
            {
                var payload = request ?? new PayOrderRequest();
                payload.OrderId = orderId;
                return await HandleAsync(payload, payments);
            })
            .Produces<OrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService payments)
    {
        var buyerId = _httpContextAccessor.HttpContext!.RequireUserName();
        CardPaymentDetails? card = null;
        if (request.Card != null)
        {
            CardBillingAddress? billing = null;
            if (request.Card.BillingAddress != null)
            {
                billing = new CardBillingAddress(
                    request.Card.BillingAddress.AddressLine1,
                    request.Card.BillingAddress.AddressLine2,
                    request.Card.BillingAddress.AdminArea2,
                    request.Card.BillingAddress.AdminArea1,
                    request.Card.BillingAddress.PostalCode,
                    request.Card.BillingAddress.CountryCode);
            }

            card = new CardPaymentDetails(
                request.Card.Number,
                request.Card.Expiry,
                request.Card.SecurityCode,
                request.Card.Name,
                billing);
        }

        var order = await payments.PayAsync(request.OrderId, buyerId, card, request.PaymentMethodId);
        return Results.Ok(OrderResponseMapper.From(order, _payPal.Currency));
    }
}

public class PayOrderRequest
{
    public int OrderId { get; set; }
    public int? PaymentMethodId { get; set; }
    public CardPaymentRequest? Card { get; set; }
}

public class CardPaymentRequest
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public BillingAddressRequest? BillingAddress { get; set; }
}

public class BillingAddressRequest
{
    public string AddressLine1 { get; set; } = string.Empty;
    public string? AddressLine2 { get; set; }
    public string AdminArea2 { get; set; } = string.Empty;
    public string? AdminArea1 { get; set; }
    public string PostalCode { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
}
