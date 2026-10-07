using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Pays the caller's order total by card, taking the money now. Safe to repeat: a second call never charges twice.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, IOrderPaymentService orderPaymentService) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();
                request.OrderId = orderId;
                request.BuyerId = buyerId;
                return await HandleAsync(request, orderPaymentService);
            })
            .Produces<PayOrderResponse>(StatusCodes.Status200OK)
            .Produces<PayOrderResponse>(StatusCodes.Status202Accepted)
            .Produces<PayOrderResponse>(StatusCodes.Status402PaymentRequired)
            .Produces<PayOrderResponse>(StatusCodes.Status409Conflict)
            .Produces<PayOrderResponse>(StatusCodes.Status422UnprocessableEntity)
            .Produces<PayOrderResponse>(StatusCodes.Status504GatewayTimeout)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService orderPaymentService)
    {
        var card = new EncryptedCard(request.EncryptedCardNumber, request.EncryptedExpiryMonth,
            request.EncryptedExpiryYear, request.EncryptedSecurityCode, request.HolderName);

        // Not tied to the caller's connection: once a payment is sent its outcome must be recorded even if the
        // shopper goes away. The service bounds the provider time itself.
        var result = await orderPaymentService.PayAsync(request.BuyerId, request.OrderId, card, CancellationToken.None);

        var response = new PayOrderResponse(request.CorrelationId())
        {
            OrderId = result.OrderId,
            Status = result.Status.ToString(),
            Message = result.Message,
            PaymentStatus = result.PaymentStatus?.ToString(),
            PspReference = result.PspReference,
            Amount = result.Amount,
            Currency = result.Currency,
            RefusalReason = result.RefusalReason
        };
        return Results.Json(response, statusCode: StatusCodeFor(result.Status));
    }

    internal static int StatusCodeFor(PayOrderStatus status) => status switch
    {
        PayOrderStatus.Paid or PayOrderStatus.AlreadyPaid => StatusCodes.Status200OK,
        PayOrderStatus.Pending => StatusCodes.Status202Accepted,
        PayOrderStatus.Refused or PayOrderStatus.ActionRequired => StatusCodes.Status402PaymentRequired,
        PayOrderStatus.Rejected or PayOrderStatus.NotPayable => StatusCodes.Status422UnprocessableEntity,
        PayOrderStatus.Invalid => StatusCodes.Status400BadRequest,
        PayOrderStatus.NotFound => StatusCodes.Status404NotFound,
        PayOrderStatus.InProgress => StatusCodes.Status409Conflict,
        PayOrderStatus.ProviderUnavailable => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status504GatewayTimeout
    };
}
