using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderPaymentEndpoints;

public class CancelOrderContext
{
    public int OrderId { get; init; }
    public CancellationToken Ct { get; init; }
}

/// <summary>
/// Operator action: cancels an order before fulfilment by releasing the held funds (void), so no money
/// ever moved.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderContext>
{
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly IPayPalPaymentGateway _gateway;

    public CancelOrderEndpoint(IRepository<OrderPayment> paymentRepository, IPayPalPaymentGateway gateway)
    {
        _paymentRepository = paymentRepository;
        _gateway = gateway;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, CancellationToken ct) =>
                await HandleAsync(new CancelOrderContext { OrderId = orderId, Ct = ct }))
            .Produces<OrderPaymentDto>()
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelOrderContext context)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpecification(context.OrderId));
        if (payment is null)
            return Results.NotFound(new { message = $"Order {context.OrderId} was not found." });

        // Idempotent: an already-cancelled order is not voided again.
        if (payment.Status == PaymentStatus.Cancelled)
            return Results.Ok(OrderPaymentDto.From(payment));

        if (payment.Status != PaymentStatus.Authorized || payment.AuthorizationId is null)
            return Results.Conflict(new { message = $"Order {context.OrderId} cannot be cancelled in its current state ({payment.Status}); only a held (authorized) order can be cancelled." });

        try
        {
            await _gateway.VoidAsync(payment.AuthorizationId, $"cancel-{payment.IdempotencyToken}", context.Ct);

            payment.RecordCancellation();
            await _paymentRepository.UpdateAsync(payment);

            return Results.Ok(OrderPaymentDto.From(payment));
        }
        catch (PaymentGatewayException ex)
        {
            return PaymentResults.FromGatewayException(ex);
        }
    }
}
