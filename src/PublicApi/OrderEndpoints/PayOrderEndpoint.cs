using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRouteRequest, ICheckoutService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public PayOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ICheckoutService checkout) =>
            {
                return await HandleAsync(new PayOrderRouteRequest(orderId, request), checkout);
            })
            .Produces<OrderActionResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRouteRequest request, ICheckoutService checkout)
    {
        var http = _httpContextAccessor.HttpContext!;
        var buyerId = http.RequireBuyerId();
        var body = request.Body;
        var hasCard = body.Card is not null && !string.IsNullOrWhiteSpace(body.Card.Number);
        var hasSaved = body.PaymentMethodId.HasValue;

        if (hasCard == hasSaved)
        {
            throw new CheckoutException(400, "Provide either card details or a saved paymentMethodId, not both.");
        }

        var order = hasSaved
            ? await checkout.PayWithSavedCardAsync(request.OrderId, buyerId, body.PaymentMethodId!.Value, http.RequestAborted)
            : await checkout.PayWithCardAsync(request.OrderId, buyerId, OrderDtoMapper.ToCardInput(body.Card!), http.RequestAborted);

        return Results.Ok(new OrderActionResponse
        {
            OrderId = order.Id,
            Order = OrderDtoMapper.ToDto(order)
        });
    }
}

public class PayOrderRouteRequest
{
    public PayOrderRouteRequest(int orderId, PayOrderRequest body)
    {
        OrderId = orderId;
        Body = body;
    }

    public int OrderId { get; }
    public PayOrderRequest Body { get; }
}
