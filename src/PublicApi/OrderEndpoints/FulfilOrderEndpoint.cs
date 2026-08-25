using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class FulfilOrderRequest
{
    public int OrderId { get; set; }
}

public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest, IPayPalPaymentService>
{
    private readonly IRepository<OrderPayment> _paymentRepository;

    public FulfilOrderEndpoint(IRepository<OrderPayment> paymentRepository)
    {
        _paymentRepository = paymentRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Administrators")]
            async (int orderId, IPayPalPaymentService paymentService) =>
            {
                var request = new FulfilOrderRequest { OrderId = orderId };
                return await HandleAsync(request, paymentService);
            })
            .Produces<object>(200)
            .Produces(404)
            .Produces(422)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(FulfilOrderRequest request, IPayPalPaymentService paymentService)
    {
        var spec = new OrderPaymentByOrderIdSpec(request.OrderId);
        var payment = await _paymentRepository.FirstOrDefaultAsync(spec);

        if (payment is null) return Results.NotFound(new { error = "Order payment not found." });

        if (payment.Status == OrderPaymentStatus.Captured)
            return Results.Ok(new { orderId = request.OrderId, captureId = payment.CaptureId, status = payment.Status.ToString() });

        if (payment.Status != OrderPaymentStatus.Authorized)
            return Results.UnprocessableEntity(new { error = $"Cannot fulfil an order in state: {payment.Status}." });

        CaptureResult result;
        try
        {
            result = await paymentService.CaptureAsync(payment, CancellationToken.None);
        }
        catch (PayPalException ex)
        {
            return Results.Problem(ex.Message, statusCode: ex.StatusCode);
        }

        payment.SetCaptured(result.CaptureId, result.CapturedAmount, result.PayPalFee, result.NetAmount, $"{request.OrderId}-capture");
        await _paymentRepository.UpdateAsync(payment);

        return Results.Ok(new
        {
            orderId = request.OrderId,
            captureId = result.CaptureId,
            capturedAmount = result.CapturedAmount,
            status = payment.Status.ToString()
        });
    }
}
