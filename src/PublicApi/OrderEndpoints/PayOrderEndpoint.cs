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
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IPayPalPaymentsClient _payPal;

    public PayOrderEndpoint(IHttpContextAccessor httpContextAccessor, IPayPalPaymentsClient payPal)
    {
        _httpContextAccessor = httpContextAccessor;
        _payPal = payPal;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, PayOrderRequest body, IOrderPaymentService orders) =>
                await HandleAsync(new PayOrderRouteRequest(orderId, body), orders))
            .Produces<OrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRouteRequest request, IOrderPaymentService orders)
    {
        var buyerId = CallerIdentity.GetBuyerId(_httpContextAccessor.HttpContext);
        var order = await orders.PayAsync(
            buyerId,
            request.OrderId,
            request.Body.Card?.ToInput(),
            request.Body.PaymentMethodId);
        return Results.Ok(OrderResponse.From(order, _payPal.Currency));
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
