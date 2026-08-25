using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.Payment;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, IRepository<Order>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                       AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IRepository<Order> orderRepo,
                   IRepository<PaymentRecord> paymentRepo,
                   IPayPalService payPalService) =>
            {
                var request = new CancelOrderRequest { OrderId = orderId };
                return await HandleAsync(request, orderRepo, paymentRepo, payPalService);
            })
            .Produces(200)
            .Produces(404)
            .Produces(409)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CancelOrderRequest request, IRepository<Order> repo)
        => throw new System.NotSupportedException();

    private static async Task<IResult> HandleAsync(
        CancelOrderRequest request,
        IRepository<Order> orderRepo,
        IRepository<PaymentRecord> paymentRepo,
        IPayPalService payPalService)
    {
        var order = await orderRepo.GetByIdAsync(request.OrderId);
        if (order == null)
            return Results.NotFound(new { error = "Order not found." });

        if (order.PaymentStatus != OrderPaymentStatus.Authorized)
            return Results.Conflict(new { error = $"Order cannot be cancelled in status '{order.PaymentStatus}'." });

        var paymentRecordSpec = new PaymentRecordByOrderIdSpec(request.OrderId);
        var paymentRecord = (await paymentRepo.ListAsync(paymentRecordSpec)).FirstOrDefault();
        if (paymentRecord?.AuthorizationId == null)
            return Results.Problem("Payment record missing authorization.", statusCode: 500);

        try
        {
            await payPalService.VoidAuthorizationAsync(paymentRecord.AuthorizationId);

            paymentRecord.SetCancelled();
            await paymentRepo.UpdateAsync(paymentRecord);

            order.SetPaymentStatus(OrderPaymentStatus.Cancelled);
            await orderRepo.UpdateAsync(order);

            return Results.Ok(new { message = "Order cancelled and authorization voided." });
        }
        catch (PayPalProviderException ex)
        {
            return Results.Problem(ex.Message, statusCode: 502);
        }
        catch (System.Text.Json.JsonException)
        {
            return Results.Problem("PayPal returned an unreadable response.", statusCode: 502);
        }
    }
}
