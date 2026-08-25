using System;
using System.Threading;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.PayPalService;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CancelOrderResponse : BaseResponse
{
    public CancelOrderResponse(System.Guid correlationId) : base(correlationId) { }
    public string Status { get; set; } = "";
}

public class CancelOrderEndpoint : IEndpoint<IResult>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IRepository<Order> orderRepo,
                   IPayPalService paypal, CancellationToken ct) =>
            {
                var order = await orderRepo.FirstOrDefaultAsync(new OrderWithPaymentSpec(orderId), ct);
                if (order == null) return Results.NotFound("Order not found.");
                if (order.Status != OrderStatus.Authorized)
                    return Results.BadRequest($"Only Authorized orders can be cancelled. Current: {order.Status}.");

                var payment = order.Payment;
                if (payment == null) return Results.BadRequest("Order has no payment record.");

                await paypal.VoidAsync(payment.AuthorizationId, ct);
                order.SetCancelled();
                await orderRepo.UpdateAsync(order, ct);

                return Results.Ok(new CancelOrderResponse(Guid.NewGuid()) { Status = order.Status.ToString() });
            })
            .Produces<CancelOrderResponse>()
            .Produces(400).Produces(404)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync() => Task.FromResult<IResult>(Results.StatusCode(501));
}
