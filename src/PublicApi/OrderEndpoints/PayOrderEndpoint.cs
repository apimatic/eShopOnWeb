using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;
using CorePayRequest = Microsoft.eShopWeb.ApplicationCore.Interfaces.PayOrderRequest;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IPaymentSettings _paymentSettings;

    public PayOrderEndpoint(IHttpContextAccessor httpContextAccessor, IPaymentSettings paymentSettings)
    {
        _httpContextAccessor = httpContextAccessor;
        _paymentSettings = paymentSettings;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, PayOrderRequest request, IOrderPaymentService orders) =>
                await HandleAsync(orderId, request, orders))
            .Produces<CreateOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService orders) =>
        HandleAsync(0, request, orders);

    private async Task<IResult> HandleAsync(int orderId, PayOrderRequest request, IOrderPaymentService orders)
    {
        var buyerId = _httpContextAccessor.HttpContext!.RequireBuyerId();
        if (request.Card != null)
        {
            ValidateCard(request.Card);
        }

        var order = await orders.PayAsync(new CorePayRequest(
            orderId,
            buyerId,
            request.Card?.ToCardPaymentSource(),
            request.PaymentMethodId));

        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Order = OrderDto.From(order, _paymentSettings.Currency)
        };
        return Results.Ok(response);
    }

    private static void ValidateCard(CardDetailsRequest card)
    {
        var digits = new string((card.Number ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length is < 13 or > 19)
        {
            throw new ApplicationCore.Exceptions.PaymentException("Card number must contain 13 to 19 digits.");
        }

        if (string.IsNullOrWhiteSpace(card.Expiry) || card.Expiry.Length != 7)
        {
            throw new ApplicationCore.Exceptions.PaymentException("Card expiry must be in YYYY-MM format.");
        }

        var cvc = new string((card.SecurityCode ?? string.Empty).Where(char.IsDigit).ToArray());
        if (cvc.Length is < 3 or > 4)
        {
            throw new ApplicationCore.Exceptions.PaymentException("Card security code must be 3 or 4 digits.");
        }

        if (string.IsNullOrWhiteSpace(card.Name))
        {
            throw new ApplicationCore.Exceptions.PaymentException("Cardholder name is required.");
        }
    }
}
