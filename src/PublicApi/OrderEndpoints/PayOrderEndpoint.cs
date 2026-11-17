using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Authorizes (holds) the order total. Money is not captured until fulfilment.
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
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user,
                CancellationToken cancellationToken) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, user, cancellationToken);
            })
            .Produces<OrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        var command = new PayOrderCommand(
            request.SavedPaymentMethodId,
            request.Card is null ? null : PaymentRequestMapper.ToCardDetails(request.Card),
            request.SecurityCode);

        var order = await _orderPaymentService.PayOrderAsync(
            user.Identity!.Name!, request.OrderId, command, cancellationToken);

        return Results.Ok(OrderResponse.From(order, request.CorrelationId(),
            "Card authorized. Funds are held until the order is fulfilled."));
    }
}
