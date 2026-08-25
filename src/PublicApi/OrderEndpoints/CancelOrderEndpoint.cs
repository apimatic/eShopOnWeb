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
using Microsoft.eShopWeb.PublicApi.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CancelOrderEndpoint : IEndpoint<IResult, int, IRepository<Order>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IRepository<Order> orderRepo, IPayPalService paypal, CancellationToken ct) =>
            {
                return await HandleAsync(orderId, orderRepo, paypal, ct);
            })
            .Produces<CancelOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IRepository<Order> orderRepo)
        => Results.StatusCode(500);

    private async Task<IResult> HandleAsync(int orderId, IRepository<Order> orderRepo,
        IPayPalService paypal, CancellationToken ct)
    {
        var spec = new OrderWithPaymentSpec(orderId);
        var order = await orderRepo.FirstOrDefaultAsync(spec, ct);

        if (order is null) return Results.NotFound();

        if (order.Status == OrderStatus.Cancelled)
            return Results.Ok(new CancelOrderResponse { OrderId = order.Id, Status = "Cancelled" });

        if (order.Status != OrderStatus.PaymentAuthorized && order.Status != OrderStatus.PendingPayment)
            return Results.BadRequest($"Order in status '{order.Status}' cannot be cancelled.");

        if (order.Payment is not null)
        {
            try
            {
                await paypal.VoidAuthorizationAsync(
                    order.Payment.AuthorizationId,
                    $"void-{orderId}",
                    ct);
            }
            catch (PayPalException ex) when (ex.StatusCode == 409)
            {
                // Already voided — idempotent, continue
            }
            catch (PayPalException ex)
            {
                return Results.Problem(
                    title: "Cancellation failed",
                    detail: ex.Message,
                    statusCode: ex.StatusCode);
            }
        }

        order.SetCancelled();
        await orderRepo.UpdateAsync(order, ct);

        return Results.Ok(new CancelOrderResponse { OrderId = order.Id, Status = "Cancelled" });
    }
}
