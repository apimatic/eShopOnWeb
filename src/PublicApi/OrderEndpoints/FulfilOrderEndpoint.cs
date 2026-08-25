using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: marks an order fulfilled. This is when the held funds are actually captured.
/// If the authorization has gone stale, it is renewed first; if it can no longer be renewed, a
/// 409 with an operator-actionable message is returned instead of failing silently.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest, IRepository<Order>>
{
    private readonly IPaymentProvider _paymentProvider;

    public FulfilOrderEndpoint(IPaymentProvider paymentProvider)
    {
        _paymentProvider = paymentProvider;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IRepository<Order> orderRepository) =>
            {
                return await HandleAsync(new FulfilOrderRequest(orderId), orderRepository);
            })
            .Produces<FulfilOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(FulfilOrderRequest request, IRepository<Order> orderRepository)
    {
        var order = await orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(request.OrderId));
        if (order is null)
        {
            return Results.NotFound();
        }

        if (order.Status != OrderStatus.PaymentAuthorized || order.Payment is null)
        {
            return Results.Conflict(new { message = $"Order {order.Id} has no active payment authorization to fulfil (status {order.Status})." });
        }

        var authorizationId = order.Payment.AuthorizationId;

        var freshness = await _paymentProvider.GetAuthorizationFreshnessAsync(authorizationId, CancellationToken.None);
        if (!freshness.IsFresh)
        {
            var reauthorization = await _paymentProvider.ReauthorizeAsync(authorizationId, $"reauth-{order.Id}-{order.IdempotencySalt:N}", CancellationToken.None);
            order.RecordReauthorization(reauthorization.AuthorizationId, reauthorization.Status, reauthorization.ExpiresAt);
            authorizationId = reauthorization.AuthorizationId;
        }

        var capture = await _paymentProvider.CaptureAsync(authorizationId, order.Total(), order.Currency, $"capture-{order.Id}-{order.IdempotencySalt:N}", CancellationToken.None);
        order.RecordCapture(capture.CaptureId, capture.Status, capture.GrossAmount, capture.FeeAmount, capture.NetAmount);

        await orderRepository.UpdateAsync(order);

        return Results.Ok(new FulfilOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Order = OrderDto.FromOrder(order)
        });
    }
}
