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

public class CancelOrderRequest
{
    public int OrderId { get; set; }
}

public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, IPayPalPaymentService>
{
    private readonly IRepository<OrderPayment> _paymentRepository;

    public CancelOrderEndpoint(IRepository<OrderPayment> paymentRepository)
    {
        _paymentRepository = paymentRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Administrators")]
            async (int orderId, IPayPalPaymentService paymentService) =>
            {
                var request = new CancelOrderRequest { OrderId = orderId };
                return await HandleAsync(request, paymentService);
            })
            .Produces<object>(200)
            .Produces(404)
            .Produces(422)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request, IPayPalPaymentService paymentService)
    {
        var spec = new OrderPaymentByOrderIdSpec(request.OrderId);
        var payment = await _paymentRepository.FirstOrDefaultAsync(spec);

        if (payment is null) return Results.NotFound(new { error = "Order payment not found." });

        if (payment.Status == OrderPaymentStatus.Voided)
            return Results.Ok(new { orderId = request.OrderId, status = payment.Status.ToString() });

        if (payment.Status != OrderPaymentStatus.Authorized)
            return Results.UnprocessableEntity(new { error = $"Cannot cancel an order in state: {payment.Status}." });

        try
        {
            await paymentService.VoidAsync(payment, CancellationToken.None);
        }
        catch (PayPalException ex)
        {
            return Results.Problem(ex.Message, statusCode: ex.StatusCode);
        }

        payment.SetVoided();
        await _paymentRepository.UpdateAsync(payment);

        return Results.Ok(new { orderId = request.OrderId, status = payment.Status.ToString() });
    }
}
