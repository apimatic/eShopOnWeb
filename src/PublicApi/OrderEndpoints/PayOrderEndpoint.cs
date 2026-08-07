using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Pays an awaiting-payment order with PayPal, using either a one-off card or one of the shopper's
/// saved cards. Idempotent: repeating the call never charges the order twice.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, ClaimsPrincipal, CancellationToken>
{
    private readonly IOrderPaymentService _orderPaymentService;

    public PayOrderEndpoint(IOrderPaymentService orderPaymentService)
    {
        _orderPaymentService = orderPaymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, CancellationToken ct) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, user, ct);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, ClaimsPrincipal user, CancellationToken ct)
    {
        var buyerId = user.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        var instruction = new PaymentInstruction(request.Card?.ToCardDetails(), request.SavedPaymentMethodId);

        var order = await _orderPaymentService.PayOrderAsync(buyerId, request.OrderId, instruction, ct);

        var response = new PayOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            PaymentStatus = order.PaymentStatus.ToString(),
            Order = order.ToDto()
        };
        return Results.Ok(response);
    }
}
