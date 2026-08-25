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
using Microsoft.eShopWeb.Infrastructure.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>Operator: voids the authorization, releasing held funds.</summary>
public class CancelOrderEndpoint : IEndpoint<IResult, int, IRepository<Order>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId,
                IRepository<Order> orderRepo,
                IRepository<OrderPayment> paymentRepo,
                IPayPalPaymentService payPalService,
                CancellationToken ct) =>
            {
                var order = await orderRepo.GetBySpecAsync(new OrderByIdWithPaymentSpec(orderId), ct);
                if (order == null)
                    return Results.NotFound(new { error = "Order not found." });

                var payment = order.Payment;
                if (payment == null || payment.PaymentStatus != PaymentStatuses.Authorized)
                    return Results.Conflict(new { error = "Order cannot be cancelled (not in Authorized state)." });

                try
                {
                    await payPalService.VoidAsync(payment.AuthorizationId!, ct);
                }
                catch (PayPalException ex) when (ex.IsClientError)
                {
                    return Results.UnprocessableEntity(new { error = ex.Message });
                }
                catch (PayPalException ex)
                {
                    return Results.Problem(ex.Message, statusCode: 502);
                }

                payment.SetVoided();
                await paymentRepo.UpdateAsync(payment, ct);

                return Results.Ok(new { orderId, status = "Cancelled" });
            })
            .Produces(200)
            .Produces(404)
            .Produces(409)
            .Produces(422)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(int request, IRepository<Order> repo)
        => throw new System.NotImplementedException();
}
