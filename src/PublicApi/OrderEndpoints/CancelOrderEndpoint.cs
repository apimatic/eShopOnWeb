using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CancelOrderResponse
{
    public string PaymentStatus { get; set; } = string.Empty;
}

public class CancelOrderEndpoint : IEndpoint<IResult, EmptyRequest, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, HttpContext ctx) =>
            {
                return await HandleAsync(new EmptyRequest(), ctx, orderId);
            })
            .Produces<CancelOrderResponse>()
            .Produces(400)
            .Produces(404)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(EmptyRequest request, HttpContext ctx)
        => HandleAsync(request, ctx, 0);

    private async Task<IResult> HandleAsync(EmptyRequest _, HttpContext ctx, int orderId)
    {
        var sp = ctx.RequestServices;
        var paymentRepo = sp.GetRequiredService<IRepository<Payment>>();
        var paypalService = sp.GetRequiredService<IPayPalService>();
        var ct = ctx.RequestAborted;

        var paymentSpec = new PaymentByOrderIdSpec(orderId);
        var payment = await paymentRepo.FirstOrDefaultAsync(paymentSpec, ct);
        if (payment is null) return Results.NotFound("Payment record not found.");
        if (payment.Status != PaymentStatus.Authorized)
            return Results.BadRequest($"Only authorized orders can be cancelled. Current status: {payment.Status}");

        try
        {
            await paypalService.VoidAsync(payment.PayPalAuthorizationId!, ct);
        }
        catch (PayPalException ex)
        {
            return Results.Problem(ex.Message, statusCode: ex.StatusCode ?? 422);
        }

        payment.RecordVoid();
        await paymentRepo.UpdateAsync(payment, ct);

        return Results.Ok(new CancelOrderResponse { PaymentStatus = payment.Status.ToString() });
    }
}
