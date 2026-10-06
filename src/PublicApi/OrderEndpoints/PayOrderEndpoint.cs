using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Pays the caller's order total by card, capturing the money immediately. Safe to repeat: an order
/// is charged at most once, however many times this is called.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, IOrderPaymentService paymentService) =>
            {
                request.OrderId = orderId;
                request.BuyerId = user.Identity!.Name!;
                return await HandleAsync(request, paymentService);
            })
            .Produces<PayOrderResponse>(StatusCodes.Status200OK)
            .Produces<PayOrderResponse>(StatusCodes.Status202Accepted)
            .Produces<PayOrderResponse>(StatusCodes.Status402PaymentRequired)
            .Produces<PayOrderResponse>(StatusCodes.Status409Conflict)
            .Produces<PayOrderResponse>(StatusCodes.Status422UnprocessableEntity)
            .Produces<PayOrderResponse>(StatusCodes.Status502BadGateway)
            .Produces<ErrorDetails>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService paymentService)
    {
        var card = request.PaymentMethod;
        var details = new EncryptedCardDetails(
            (card?.EncryptedCardNumber ?? request.EncryptedCardNumber)?.Trim() ?? string.Empty,
            (card?.EncryptedExpiryMonth ?? request.EncryptedExpiryMonth)?.Trim() ?? string.Empty,
            (card?.EncryptedExpiryYear ?? request.EncryptedExpiryYear)?.Trim() ?? string.Empty,
            (card?.EncryptedSecurityCode ?? request.EncryptedSecurityCode)?.Trim() ?? string.Empty,
            (card?.HolderName ?? request.HolderName)?.Trim() ?? string.Empty);

        if (string.IsNullOrEmpty(details.EncryptedCardNumber) || string.IsNullOrEmpty(details.EncryptedExpiryMonth) ||
            string.IsNullOrEmpty(details.EncryptedExpiryYear) || string.IsNullOrEmpty(details.EncryptedSecurityCode) ||
            string.IsNullOrEmpty(details.HolderName))
        {
            return Results.Json(new ErrorDetails
            {
                StatusCode = StatusCodes.Status400BadRequest,
                Message = "encryptedCardNumber, encryptedExpiryMonth, encryptedExpiryYear, encryptedSecurityCode and holderName are required."
            }, statusCode: StatusCodes.Status400BadRequest);
        }

        var result = await paymentService.PayAsync(request.OrderId, request.BuyerId, details, CancellationToken.None);
        if (result.Outcome == PayOrderOutcome.OrderNotFound)
        {
            return Results.NotFound();
        }

        var attempt = result.Attempt;
        var response = new PayOrderResponse(request.CorrelationId())
        {
            OrderId = result.OrderId,
            Outcome = result.Outcome.ToString(),
            Paid = result.PaymentStatus is OrderPaymentStatus.Paid or OrderPaymentStatus.PartiallyRefunded or OrderPaymentStatus.Refunded,
            PaymentStatus = result.PaymentStatus.ToString(),
            Message = result.Message,
            Amount = attempt is null ? null : MinorUnits.ToDecimal(attempt.AmountMinorUnits, attempt.Currency),
            Currency = attempt?.Currency,
            AttemptNumber = attempt?.AttemptNumber,
            PspReference = attempt?.PspReference,
            ResultCode = attempt?.ResultCode,
            RefusalReason = attempt?.RefusalReason,
            RefusalReasonCode = attempt?.RefusalReasonCode,
        };

        var statusCode = result.Outcome switch
        {
            PayOrderOutcome.Paid or PayOrderOutcome.AlreadyPaid => StatusCodes.Status200OK,
            PayOrderOutcome.Pending => StatusCodes.Status202Accepted,
            PayOrderOutcome.Refused or PayOrderOutcome.AuthenticationNotSupported => StatusCodes.Status402PaymentRequired,
            PayOrderOutcome.InProgress => StatusCodes.Status409Conflict,
            PayOrderOutcome.CardDetailsRejected or PayOrderOutcome.AmountNotChargeable => StatusCodes.Status422UnprocessableEntity,
            _ => StatusCodes.Status502BadGateway
        };
        return Results.Json(response, statusCode: statusCode);
    }
}
