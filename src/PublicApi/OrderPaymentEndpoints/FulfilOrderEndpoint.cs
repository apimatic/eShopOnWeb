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

public class FulfilOrderContext
{
    public int OrderId { get; init; }
    public CancellationToken Ct { get; init; }
}

/// <summary>
/// Operator action: marks the order fulfilled and captures (takes) the held funds. A stale authorization
/// is renewed before capture; one that cannot be renewed returns an operator-actionable 409.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderContext>
{
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly IPayPalPaymentGateway _gateway;

    public FulfilOrderEndpoint(IRepository<OrderPayment> paymentRepository, IPayPalPaymentGateway gateway)
    {
        _paymentRepository = paymentRepository;
        _gateway = gateway;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, CancellationToken ct) =>
                await HandleAsync(new FulfilOrderContext { OrderId = orderId, Ct = ct }))
            .Produces<OrderPaymentDto>()
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(FulfilOrderContext context)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpecification(context.OrderId));
        if (payment is null)
            return Results.NotFound(new { message = $"Order {context.OrderId} was not found." });

        // Idempotent: an already-captured order is not captured again.
        if (payment.CaptureId is not null)
            return Results.Ok(OrderPaymentDto.From(payment));

        if (payment.Status != PaymentStatus.Authorized || payment.AuthorizationId is null)
            return Results.Conflict(new { message = $"Order {context.OrderId} is not in an authorized state ({payment.Status})." });

        try
        {
            var result = await _gateway.CaptureAsync(
                new CaptureCommand(payment.AuthorizationId, payment.Amount, $"fulfil-{payment.IdempotencyToken}"),
                context.Ct);

            payment.RecordCapture(result.CaptureId, result.EffectiveAuthorizationId, result.Status,
                result.Gross, result.Fee, result.Net);
            await _paymentRepository.UpdateAsync(payment);

            return Results.Ok(OrderPaymentDto.From(payment));
        }
        catch (PaymentGatewayException ex)
        {
            // The order stays Authorized so the operator can retry; the failure is surfaced to them.
            return PaymentResults.FromGatewayException(ex);
        }
    }
}
