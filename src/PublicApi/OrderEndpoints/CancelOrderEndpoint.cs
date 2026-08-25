using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CancelOrderEndpoint : IEndpoint<IResult, int, IRepository<Order>>
{
    private readonly IPaymentService _paymentService;

    public CancelOrderEndpoint(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IRepository<Order> orderRepo) =>
            {
                return await HandleAsync(orderId, orderRepo);
            })
            .Produces<CancelOrderResponse>()
            .ProducesProblem(400)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IRepository<Order> orderRepo)
    {
        var order = await orderRepo.GetByIdAsync(orderId);
        if (order is null)
            return Results.NotFound();
        if (order.PaymentStatus != OrderPaymentStatus.Authorized)
            return Results.BadRequest($"Order is in '{order.PaymentStatus}' state and cannot be cancelled.");
        if (string.IsNullOrEmpty(order.AuthorizationId))
            return Results.BadRequest("Order has no authorization ID.");

        await _paymentService.VoidAuthorizationAsync(order.AuthorizationId);
        order.SetPaymentCancelled();
        await orderRepo.UpdateAsync(order);

        return Results.Ok(new CancelOrderResponse(Guid.NewGuid())
        {
            OrderId = orderId
        });
    }
}
