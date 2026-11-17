using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
    public CardRequest? Card { get; set; }
    public int? PaymentMethodId { get; set; }
    public string? Number { get; set; }
    public int? ExpirationMonth { get; set; }
    public int? ExpirationYear { get; set; }
    public string? Cvc { get; set; }
    public string? Name { get; set; }
    public AddressRequest? Address { get; set; }

    public CardRequest? ResolveCard()
    {
        if (Card is not null)
        {
            return Card;
        }

        if (string.IsNullOrWhiteSpace(Number))
        {
            return null;
        }

        return new CardRequest
        {
            Number = Number!,
            ExpirationMonth = ExpirationMonth ?? 0,
            ExpirationYear = ExpirationYear ?? 0,
            Cvc = Cvc ?? string.Empty,
            Name = Name ?? string.Empty,
            Address = Address
        };
    }
}

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, HttpContext>
{
    private readonly IPaymentService _paymentService;

    public PayOrderEndpoint(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (int orderId, PayOrderRequest request, HttpContext httpContext) =>
                {
                    request.OrderId = orderId;
                    return await HandleAsync(request, httpContext);
                })
            .Produces<OrderDto>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, HttpContext httpContext)
    {
        var data = new PayOrderData(request.OrderId,
            httpContext.User.GetBuyerId(),
            request.ResolveCard().ToCardData(),
            request.PaymentMethodId);
        var order = await _paymentService.PayOrderAsync(data, httpContext.RequestAborted);
        return Results.Ok(order);
    }
}
