using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: cancels an order. The shopper is told, and any delivery follow-up that has not yet
/// gone out is called off with the provider so it never reaches the customer.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, OrderIdRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderNotificationService service, CancellationToken ct) =>
            {
                return await ExecuteAsync(new OrderIdRequest { OrderId = orderId }, service, ct);
            })
            .Produces<OrderActionResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(OrderIdRequest request, IOrderNotificationService service)
        => ExecuteAsync(request, service, CancellationToken.None);

    private static async Task<IResult> ExecuteAsync(OrderIdRequest request, IOrderNotificationService service, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        var order = await service.CancelOrderAsync(request.OrderId, cts.Token);
        return order is null
            ? Results.NotFound()
            : Results.Ok(new OrderActionResponse { OrderId = order.Id, Status = "cancelled" });
    }
}
