using System.Security.Claims;
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

public record PayOrderRequest(CardInput? Card, int? SavedPaymentMethodId);

public class PayOrderContext
{
    public int OrderId { get; init; }
    public string BuyerId { get; init; } = string.Empty;
    public PayOrderRequest Request { get; init; } = new(null, null);
    public CancellationToken Ct { get; init; }
}

/// <summary>
/// Authorizes (holds) the order total. Pays with either one-off card details or one of the shopper's
/// saved cards. Idempotent in effect: an already-authorized order is not authorized again.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderContext>
{
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IPayPalPaymentGateway _gateway;

    public PayOrderEndpoint(
        IRepository<OrderPayment> paymentRepository,
        IRepository<SavedCard> savedCardRepository,
        IPayPalPaymentGateway gateway)
    {
        _paymentRepository = paymentRepository;
        _savedCardRepository = savedCardRepository;
        _gateway = gateway;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, CancellationToken ct) =>
                await HandleAsync(new PayOrderContext { OrderId = orderId, BuyerId = user.GetBuyerId(), Request = request, Ct = ct }))
            .Produces<OrderPaymentDto>()
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderContext context)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpecification(context.OrderId));
        if (payment is null || payment.BuyerId != context.BuyerId)
            return Results.NotFound(new { message = $"Order {context.OrderId} was not found." });

        // Idempotent: a double-click never authorizes twice.
        if (payment.IsAuthorized)
            return Results.Ok(OrderPaymentDto.From(payment));

        if (payment.Status is not (PaymentStatus.PendingPayment or PaymentStatus.Failed))
            return Results.Conflict(new { message = $"Order {context.OrderId} cannot be paid in its current state ({payment.Status})." });

        string? vaultId = null;
        CardDetails? card = null;

        if (context.Request.SavedPaymentMethodId is { } savedId)
        {
            var savedCard = await _savedCardRepository.GetByIdAsync(savedId);
            if (savedCard is null || savedCard.BuyerId != context.BuyerId)
                return Results.NotFound(new { message = "The specified saved payment method was not found." });
            vaultId = savedCard.VaultId;
        }
        else if (context.Request.Card is not null)
        {
            card = context.Request.Card.ToCardDetails();
        }
        else
        {
            return Results.BadRequest(new { message = "Provide either card details or a saved payment method id." });
        }

        try
        {
            var result = await _gateway.AuthorizeAsync(
                new AuthorizeCommand(payment.OrderId, payment.Amount, $"pay-{payment.IdempotencyToken}", card, vaultId),
                context.Ct);

            payment.RecordAuthorization(result.PayPalOrderId, result.AuthorizationId, result.Status);
            await _paymentRepository.UpdateAsync(payment);

            return Results.Ok(OrderPaymentDto.From(payment));
        }
        catch (PaymentGatewayException ex)
        {
            payment.MarkFailed(ex.Message);
            await _paymentRepository.UpdateAsync(payment);
            return PaymentResults.FromGatewayException(ex);
        }
    }
}
