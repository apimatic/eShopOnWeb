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

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, ICheckoutService>
{
    private readonly IHttpContextAccessor _http;

    public PayOrderEndpoint(IHttpContextAccessor http)
    {
        _http = http;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ICheckoutService checkout) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, checkout);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, ICheckoutService checkout)
    {
        var buyerId = HttpUser.RequireBuyerId(_http.HttpContext!);
        var ct = _http.HttpContext!.RequestAborted;
        var hasCard = request.Card is not null;
        var hasSaved = request.PaymentMethodId is > 0;
        if (hasCard == hasSaved)
        {
            throw new CheckoutException("Provide either card details or a saved paymentMethodId, not both.", 400);
        }

        var order = hasSaved
            ? await checkout.PayWithSavedCardAsync(request.OrderId, buyerId, request.PaymentMethodId!.Value, ct)
            : await checkout.PayWithCardAsync(request.OrderId, buyerId, request.Card!.ToInput(), ct);

        return Results.Ok(new PayOrderResponse
        {
            OrderId = order.Id,
            Order = OrderDto.From(order)
        });
    }
}
