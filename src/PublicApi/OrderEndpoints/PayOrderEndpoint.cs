using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Payment for an order: either a one-off <see cref="Card"/> (optionally saved via <see cref="SaveCard"/>)
/// or one of the shopper's <see cref="SavedCardId"/> vaulted cards. Supply exactly one.
/// </summary>
public class PayOrderRequest
{
    public CardRequest? Card { get; set; }
    public int? SavedCardId { get; set; }
    public bool SaveCard { get; set; }
}

/// <summary>
/// Authorizes (holds) the order total. No money is taken yet — that happens at fulfilment. Shopper-scoped:
/// only the order's owner may pay it. Idempotent: a double-click never authorizes twice.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, int, PayOrderRequest, IOrderPaymentService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IPayPalPaymentService _payPal;

    public PayOrderEndpoint(IHttpContextAccessor httpContextAccessor, IPayPalPaymentService payPal)
    {
        _httpContextAccessor = httpContextAccessor;
        _payPal = payPal;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, IOrderPaymentService orderPaymentService) =>
                await HandleAsync(orderId, request, orderPaymentService))
            .Produces<OrderDto>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, PayOrderRequest request,
        IOrderPaymentService orderPaymentService)
    {
        var buyerId = _httpContextAccessor.HttpContext!.GetBuyerId();

        var instrument = new PaymentInstrument
        {
            Card = request.Card?.ToCardInput(),
            SavedCardId = request.SavedCardId,
            SaveCard = request.SaveCard
        };

        var order = await orderPaymentService.AuthorizeAsync(buyerId, orderId, instrument);
        return Results.Ok(OrderDtoMapper.ToDto(order, _payPal.Currency));
    }
}
