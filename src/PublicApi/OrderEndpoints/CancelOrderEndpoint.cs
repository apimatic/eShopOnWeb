using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CancelOrderRequest
{
    public int OrderId { get; set; }
}

public class CancelOrderResponse
{
    public int OrderId { get; set; }
    public string PaymentStatus { get; set; } = "";
}

public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, IRepository<Order>>
{
    private readonly IRepository<OrderPayment> _paymentRepo;
    private readonly IPayPalClient _paypal;

    public CancelOrderEndpoint(IRepository<OrderPayment> paymentRepo, IPayPalClient paypal)
    {
        _paymentRepo = paymentRepo;
        _paypal = paypal;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme,
                       Roles = "Administrators")]
            async (int orderId, IRepository<Order> orderRepo, CancellationToken ct) =>
            {
                return await HandleAsync(new CancelOrderRequest { OrderId = orderId }, orderRepo, ct);
            })
            .Produces<CancelOrderResponse>()
            .Produces(400)
            .Produces(404)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CancelOrderRequest request, IRepository<Order> repository)
        => HandleAsync(request, repository, default);

    private async Task<IResult> HandleAsync(CancelOrderRequest request,
        IRepository<Order> orderRepo, CancellationToken ct)
    {
        var order = await orderRepo.GetByIdAsync(request.OrderId, ct);
        if (order == null)
            return Results.NotFound();

        var spec = new OrderPaymentByOrderIdSpec(request.OrderId);
        var payment = await _paymentRepo.FirstOrDefaultAsync(spec, ct);
        if (payment == null)
            return Results.Problem("Payment record not found.");

        // Idempotency: already cancelled
        if (payment.Status == OrderPaymentStatus.Cancelled)
            return Results.Ok(new CancelOrderResponse
            {
                OrderId = order.Id,
                PaymentStatus = payment.Status.ToString()
            });

        if (payment.Status == OrderPaymentStatus.PendingPayment)
        {
            // No hold placed yet — just mark cancelled
            payment.MarkCancelled();
            await _paymentRepo.UpdateAsync(payment, ct);
            return Results.Ok(new CancelOrderResponse
            {
                OrderId = order.Id,
                PaymentStatus = payment.Status.ToString()
            });
        }

        if (payment.Status != OrderPaymentStatus.Authorized)
            return Results.BadRequest(new
            {
                error = $"Order in status '{payment.Status}' cannot be cancelled. " +
                        "Only orders awaiting payment or with an active authorization can be cancelled. " +
                        "Use the refund endpoint for fulfilled orders."
            });

        try
        {
            await _paypal.VoidAuthorizationAsync(payment.AuthorizationId!, ct);
            payment.MarkCancelled();
            await _paymentRepo.UpdateAsync(payment, ct);

            return Results.Ok(new CancelOrderResponse
            {
                OrderId = order.Id,
                PaymentStatus = payment.Status.ToString()
            });
        }
        catch (PayPalException ex)
        {
            return Results.BadRequest(new { error = ex.Message, code = ex.PayPalErrorName });
        }
    }
}
