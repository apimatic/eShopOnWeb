using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class FulfilOrderEndpoint : IEndpoint<IResult, IRepository<Order>, IRepository<PaymentReference>, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
             AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IRepository<Order> orderRepo, IRepository<PaymentReference> paymentRepo, IPaymentService paymentService) =>
            {
                return await HandleAsync(orderRepo, paymentRepo, paymentService, orderId);
            })
            .Produces<FulfilOrderResponse>()
            .WithName("FulfilOrder")
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(IRepository<Order> orderRepo, IRepository<PaymentReference> paymentRepo,
        IPaymentService paymentService, int orderId)
    {
        var order = await orderRepo.GetByIdAsync(orderId);
        if (order == null)
            return Results.NotFound("Order not found");

        var paymentRef = (await paymentRepo.ListAsync(p => p.OrderId == orderId)).FirstOrDefault();
        if (paymentRef == null)
            return Results.BadRequest("No payment reference found for order");

        if (paymentRef.State != PaymentState.Authorized)
            return Results.BadRequest("Order is not in authorized state");

        var result = await paymentService.CapturePaymentAsync(paymentRef);
        if (!result.Success)
            return Results.BadRequest(new { error = result.ErrorMessage });

        paymentRef.SetCapture(result.CaptureId!, result.CapturedAmount, result.PaypalFee);
        await paymentRepo.SaveChangesAsync();

        return Results.Ok(new FulfilOrderResponse
        {
            CaptureId = result.CaptureId,
            CapturedAmount = result.CapturedAmount,
            PaypalFee = result.PaypalFee,
            NetAmount = result.CapturedAmount - result.PaypalFee
        });
    }
}

public record FulfilOrderResponse
{
    public string? CaptureId { get; set; }
    public decimal CapturedAmount { get; set; }
    public decimal PaypalFee { get; set; }
    public decimal NetAmount { get; set; }
}
