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
using Microsoft.eShopWeb.Infrastructure.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CancelOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS,
                       AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, HttpContext ctx,
                   IRepository<Order> orderRepo,
                   PayPalClient paypal) =>
            {
                var spec = new OrderWithPaymentSpec(orderId);
                var order = await orderRepo.GetBySpecAsync(spec);
                if (order == null)
                    return Results.NotFound(new { error = "Order not found." });

                if (order.PaymentStatus != PaymentStatus.Authorized)
                    return Results.UnprocessableEntity(new
                    {
                        error = $"Order cannot be cancelled in its current state: {order.PaymentStatus}. " +
                                "Only authorized orders can be cancelled."
                    });

                try
                {
                    await paypal.VoidAuthorizationAsync(order.PayPalAuthorizationId!);
                }
                catch (PayPalException ex) when (
                    ex.PayPalName == "AUTHORIZATION_ALREADY_COMPLETED" ||
                    ex.PayPalName == "AUTHORIZATION_VOIDED")
                {
                    // Already voided — treat as success (idempotent)
                }
                catch (PayPalException ex)
                {
                    return Results.UnprocessableEntity(new
                    {
                        error = $"Failed to void authorization: {ex.Message}",
                        paypalCode = ex.PayPalName
                    });
                }

                order.MarkCancelled();
                await orderRepo.UpdateAsync(order);

                return Results.NoContent();
            })
            .Produces(204)
            .ProducesProblem(422)
            .WithTags("OrderEndpoints");
    }
}
