using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Pays the caller's order total by card, taking the money now. Paying an order twice never charges twice.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService>
{
    // Generous for Adyen's encrypted blobs, small enough to keep junk out.
    private const int MaxEncryptedFieldLength = 8192;
    private const int MaxHolderNameLength = 256;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, IOrderPaymentService paymentService) =>
            {
                request.OrderId = orderId;
                request.BuyerId = user.Identity?.Name ?? string.Empty;
                return await HandleAsync(request, paymentService);
            })
            .Produces<PayOrderResponse>()
            .Produces<PayOrderResponse>(StatusCodes.Status202Accepted)
            .Produces<PayOrderResponse>(StatusCodes.Status402PaymentRequired)
            .Produces<PayOrderResponse>(StatusCodes.Status409Conflict)
            .Produces<PayOrderResponse>(StatusCodes.Status422UnprocessableEntity)
            .ProducesValidationProblem()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService paymentService)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
            return Results.Unauthorized();

        var card = ReadCard(request, out var errors);
        if (card is null)
            return Results.ValidationProblem(errors);

        PayOrderResult result;
        try
        {
            result = await paymentService.PayAsync(request.OrderId, request.BuyerId, card);
        }
        catch (PaymentConcurrencyException)
        {
            return Results.Json(new PayOrderResponse(request.CorrelationId())
            {
                OrderId = request.OrderId,
                Outcome = PayOrderOutcome.InProgress.ToString(),
                Message = "This order is being updated by another request. Please try again in a moment.",
            }, statusCode: StatusCodes.Status409Conflict);
        }

        var response = new PayOrderResponse(request.CorrelationId())
        {
            OrderId = result.OrderId,
            Outcome = result.Outcome.ToString(),
            PaymentStatus = result.PaymentStatus?.ToString(),
            Message = result.Message,
            Payment = result.Attempt is null ? null : OrderDtoMapper.ToDto(result.Attempt),
        };

        var statusCode = result.Outcome switch
        {
            PayOrderOutcome.Paid or PayOrderOutcome.AlreadyPaid => StatusCodes.Status200OK,
            PayOrderOutcome.Declined => StatusCodes.Status402PaymentRequired,
            PayOrderOutcome.InvalidCard or PayOrderOutcome.Invalid => StatusCodes.Status422UnprocessableEntity,
            PayOrderOutcome.Pending or PayOrderOutcome.Unknown => StatusCodes.Status202Accepted,
            PayOrderOutcome.InProgress => StatusCodes.Status409Conflict,
            PayOrderOutcome.NotFound => StatusCodes.Status404NotFound,
            _ => StatusCodes.Status503ServiceUnavailable,
        };
        return Results.Json(response, statusCode: statusCode);
    }

    private static EncryptedCard? ReadCard(PayOrderRequest request, out Dictionary<string, string[]> errors)
    {
        var nested = request.PaymentMethod;
        var number = request.EncryptedCardNumber ?? nested?.EncryptedCardNumber;
        var month = request.EncryptedExpiryMonth ?? nested?.EncryptedExpiryMonth;
        var year = request.EncryptedExpiryYear ?? nested?.EncryptedExpiryYear;
        var cvc = request.EncryptedSecurityCode ?? nested?.EncryptedSecurityCode;
        var holder = request.HolderName ?? nested?.HolderName;

        errors = new Dictionary<string, string[]>();
        Require(errors, "encryptedCardNumber", number, MaxEncryptedFieldLength);
        Require(errors, "encryptedExpiryMonth", month, MaxEncryptedFieldLength);
        Require(errors, "encryptedExpiryYear", year, MaxEncryptedFieldLength);
        Require(errors, "encryptedSecurityCode", cvc, MaxEncryptedFieldLength);
        Require(errors, "holderName", holder, MaxHolderNameLength);
        if (nested?.Type is { } type && !string.Equals(type, "scheme", System.StringComparison.OrdinalIgnoreCase))
            errors["paymentMethod.type"] = new[] { "Only card payments (type 'scheme') are supported." };

        return errors.Count > 0 ? null : new EncryptedCard(number!, month!, year!, cvc!, holder!.Trim());
    }

    private static void Require(Dictionary<string, string[]> errors, string field, string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            errors[field] = new[] { $"{field} is required." };
        else if (value.Length > maxLength)
            errors[field] = new[] { $"{field} must be at most {maxLength} characters." };
    }
}
