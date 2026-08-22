using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRouteRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, PayOrderRequest body, IOrderPaymentService orders, HttpContext http) =>
            {
                return await HandleAsync(new PayOrderRouteRequest
                {
                    OrderId = orderId,
                    BuyerId = CreateOrderEndpoint.RequireUserName(http.User),
                    Body = body ?? new PayOrderRequest()
                }, orders);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRouteRequest request, IOrderPaymentService orders)
    {
        var card = request.Body.Card?.ToCardDetails();
        var order = await orders.PayAsync(request.OrderId, request.BuyerId, card, request.Body.PaymentMethodId);
        return Results.Ok(new PayOrderResponse { Order = OrderDto.From(order) });
    }
}

public class PayOrderRouteRequest
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public PayOrderRequest Body { get; set; } = new();
}

public class PayOrderRequest
{
    public int? PaymentMethodId { get; set; }
    public CardRequest? Card { get; set; }
}

public class PayOrderResponse
{
    public OrderDto Order { get; set; } = new();
}
