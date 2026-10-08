using System.Security.Claims;
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
/// Pays the caller's order total by card, taking the money now. Repeating the request never charges twice.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, HttpContext httpContext, IOrderPaymentService orderPaymentService) =>
            {
                request.OrderId = orderId;
                request.BuyerId = user.Identity?.Name ?? string.Empty;
                request.ReturnUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}/api/orders/{orderId}";
                return await HandleAsync(request, orderPaymentService);
            })
            .Produces<PayOrderResponse>()
            .Produces<PayOrderResponse>(StatusCodes.Status400BadRequest)
            .Produces<PayOrderResponse>(StatusCodes.Status402PaymentRequired)
            .Produces<PayOrderResponse>(StatusCodes.Status404NotFound)
            .Produces<PayOrderResponse>(StatusCodes.Status409Conflict)
            .Produces<PayOrderResponse>(StatusCodes.Status422UnprocessableEntity)
            .Produces<PayOrderResponse>(StatusCodes.Status502BadGateway)
            .Produces<PayOrderResponse>(StatusCodes.Status504GatewayTimeout)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService orderPaymentService)
    {
        var response = new PayOrderResponse(request.CorrelationId()) { OrderId = request.OrderId };
        if (string.IsNullOrEmpty(request.BuyerId))
            return Results.Unauthorized();

        if (string.IsNullOrWhiteSpace(request.EncryptedCardNumber)
            || string.IsNullOrWhiteSpace(request.EncryptedExpiryMonth)
            || string.IsNullOrWhiteSpace(request.EncryptedExpiryYear)
            || string.IsNullOrWhiteSpace(request.EncryptedSecurityCode)
            || string.IsNullOrWhiteSpace(request.HolderName))
        {
            response.Outcome = "InvalidRequest";
            response.Message = "encryptedCardNumber, encryptedExpiryMonth, encryptedExpiryYear, encryptedSecurityCode and holderName are all required.";
            return Results.BadRequest(response);
        }

        var card = new EncryptedCard(request.EncryptedCardNumber, request.EncryptedExpiryMonth, request.EncryptedExpiryYear,
            request.EncryptedSecurityCode, request.HolderName.Trim());

        var result = await orderPaymentService.PayOrderAsync(new PayOrderCommand(request.OrderId, request.BuyerId, card, request.ReturnUrl));

        response.Outcome = result.Outcome.ToString();
        response.Message = result.Message;
        if (result.Order is { } order)
        {
            response.Amount = order.Total();
            response.Currency = order.Currency;
            response.Payment = PaymentDto.From(order, result.Attempt);
            response.PspReference = order.PaymentPspReference ?? result.Attempt?.PspReference;
        }
        if (result.Attempt is { } attempt)
        {
            response.ResultCode = attempt.ResultCode;
            response.RefusalReason = attempt.RefusalReason;
        }

        var status = result.Outcome switch
        {
            PayOrderOutcome.Paid or PayOrderOutcome.AlreadyPaid => StatusCodes.Status200OK,
            PayOrderOutcome.OrderNotFound => StatusCodes.Status404NotFound,
            PayOrderOutcome.PaymentInProgress => StatusCodes.Status409Conflict,
            PayOrderOutcome.Declined => StatusCodes.Status402PaymentRequired,
            PayOrderOutcome.InvalidCard or PayOrderOutcome.CannotCharge => StatusCodes.Status422UnprocessableEntity,
            PayOrderOutcome.ProcessorTimeout => StatusCodes.Status504GatewayTimeout,
            _ => StatusCodes.Status502BadGateway
        };
        return Results.Json(response, statusCode: status);
    }
}
