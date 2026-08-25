using System;
using System.Net;
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
using Microsoft.eShopWeb.Infrastructure.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IRepository<Order>>
{
    private readonly IPayPalPaymentService _payPal;

    public PayOrderEndpoint(IPayPalPaymentService payPal)
    {
        _payPal = payPal;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, PayOrderRequest request, IRepository<Order> orderRepo, HttpContext ctx, CancellationToken ct) =>
            {
                request.OrderId = orderId;
                request.BuyerId = ctx.User.Identity?.Name ?? string.Empty;
                return await HandleAsync(request, orderRepo);
            })
            .Produces<PayOrderResponse>()
            .Produces(400)
            .Produces(409)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IRepository<Order> orderRepo)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
            return Results.Unauthorized();

        var spec = new OrderWithItemsByIdSpec(request.OrderId);
        var order = await orderRepo.FirstOrDefaultAsync(spec);

        if (order == null || order.BuyerId != request.BuyerId)
            return Results.NotFound();

        if (order.PaymentStatus == PaymentStatus.Authorized)
        {
            // Already authorized — idempotent success
            return Results.Ok(new PayOrderResponse(request.CorrelationId())
            {
                AuthorizationId = order.AuthorizationId,
                Status = order.PaymentStatus.ToString()
            });
        }

        if (order.PaymentStatus != PaymentStatus.AwaitingPayment)
            return Results.Conflict($"Order cannot be paid in current state: {order.PaymentStatus}");

        if (request.SavedCardTokenId == null && request.Card == null)
            return Results.BadRequest("Provide card details or a saved card token.");

        ApplicationCore.Interfaces.CardDetails? card = null;
        if (request.Card != null)
        {
            card = new ApplicationCore.Interfaces.CardDetails(
                Number: request.Card.Number,
                Expiry: request.Card.Expiry,
                SecurityCode: request.Card.SecurityCode,
                Name: request.Card.CardholderName,
                BillingCountryCode: request.Card.BillingCountryCode ?? "US");
        }

        var idempotencyKey = $"pay-{request.OrderId}";

        try
        {
            var result = await _payPal.AuthorizeAsync(
                orderId: request.OrderId,
                amount: order.Total(),
                card: card,
                savedCardTokenId: request.SavedCardTokenId,
                idempotencyKey: idempotencyKey);

            order.RecordAuthorization(result.PayPalOrderId, result.AuthorizationId);
            await orderRepo.UpdateAsync(order);

            return Results.Ok(new PayOrderResponse(request.CorrelationId())
            {
                AuthorizationId = result.AuthorizationId,
                Status = order.PaymentStatus.ToString()
            });
        }
        catch (PayPalOperationException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            return Results.UnprocessableEntity(new { error = ex.Message });
        }
        catch (PayPalOperationException ex)
        {
            return Results.Problem(
                title: "Payment processing error",
                detail: ex.Message,
                statusCode: (int)ex.StatusCode);
        }
    }
}

public class PayOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public string? SavedCardTokenId { get; set; }
    public CardPaymentDetails? Card { get; set; }
}

public class CardPaymentDetails
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
    public string? BillingCountryCode { get; set; }
}

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(Guid correlationId) : base(correlationId) { }
    public string? AuthorizationId { get; set; }
    public string Status { get; set; } = string.Empty;
}
