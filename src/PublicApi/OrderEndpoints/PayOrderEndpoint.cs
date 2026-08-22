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

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, ICheckoutService>
{
    private readonly IPaymentSettings _paymentSettings;

    public PayOrderEndpoint(IPaymentSettings paymentSettings)
    {
        _paymentSettings = paymentSettings;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, PayOrderRequest request, ClaimsPrincipal user, ICheckoutService checkout) =>
            {
                request.OrderId = orderId;
                request.BuyerId = user.GetBuyerId();
                return await HandleAsync(request, checkout);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, ICheckoutService checkout)
    {
        var payment = await checkout.PayAsync(
            request.BuyerId,
            request.OrderId,
            OrderDtoMapper.ToCard(request.ResolveCard()),
            request.PaymentMethodId);

        return Results.Ok(new PayOrderResponse
        {
            OrderId = payment.OrderId,
            Payment = OrderDtoMapper.MapPayment(payment),
            Currency = payment.Currency ?? _paymentSettings.Currency
        });
    }
}

public class PayOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public CardRequest? Card { get; set; }
    public int? PaymentMethodId { get; set; }
    public string? Number { get; set; }
    public string? Expiry { get; set; }
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public BillingAddressRequest? BillingAddress { get; set; }

    public CardRequest? ResolveCard() => Card ?? (string.IsNullOrWhiteSpace(Number)
        ? null
        : new CardRequest
        {
            Number = Number,
            Expiry = Expiry,
            SecurityCode = SecurityCode,
            Name = Name,
            BillingAddress = BillingAddress
        });
}

public class PayOrderResponse
{
    public int OrderId { get; set; }
    public string Currency { get; set; } = string.Empty;
    public PaymentDto Payment { get; set; } = new();
}
