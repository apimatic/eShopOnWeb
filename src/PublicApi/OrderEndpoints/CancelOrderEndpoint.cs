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
/// Operator action: cancels the order before fulfilment, releasing the shopper's held
/// funds — no money ever moves. Idempotent: cancelling twice returns the cancelled state.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, int>
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IPaymentGateway _paymentGateway;

    public CancelOrderEndpoint(IRepository<Order> orderRepository, IPaymentGateway paymentGateway)
    {
        _orderRepository = orderRepository;
        _paymentGateway = paymentGateway;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, CancellationToken ct) =>
            {
                return await HandleAsync(orderId, ct);
            })
            .Produces<CancelOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(int orderId) => HandleAsync(orderId, CancellationToken.None);

    private async Task<IResult> HandleAsync(int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentSpecification(orderId), ct);
        if (order is null)
        {
            return Results.NotFound();
        }

        var response = new CancelOrderResponse { OrderId = order.Id };

        if (order.Status == OrderStatus.Cancelled)
        {
            response.Status = order.Status.ToString();
            response.Payment = PaymentDto.FromOrder(order);
            return Results.Ok(response);
        }

        // MarkCancelled throws a PaymentStateException (409) for fulfilled orders.
        if (order.AuthorizationId is not null)
        {
            var authorization = await _paymentGateway.GetAuthorizationAsync(order.AuthorizationId, ct);
            if (authorization.Status != "VOIDED")
            {
                await _paymentGateway.VoidAuthorizationAsync(
                    order.AuthorizationId, PaymentKeys.VoidKey(order.Id), ct);
            }
            order.MarkCancelled("VOIDED");
        }
        else
        {
            order.MarkCancelled(null);
        }

        await _orderRepository.UpdateAsync(order, ct);

        response.Status = order.Status.ToString();
        response.Payment = PaymentDto.FromOrder(order);
        return Results.Ok(response);
    }
}
