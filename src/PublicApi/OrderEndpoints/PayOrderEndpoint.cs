using System;
using System.Threading;
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
/// Authorizes (holds) the order total with PayPal — either with card details
/// for a one-off payment, or with one of the shopper's saved cards.
/// The money is not taken until fulfilment.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest>
{
    private readonly IPaymentService _paymentService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public PayOrderEndpoint(IPaymentService paymentService, IHttpContextAccessor httpContextAccessor)
    {
        _paymentService = paymentService;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, CancellationToken ct) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, ct);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(PayOrderRequest request) => HandleAsync(request, CancellationToken.None);

    public async Task<IResult> HandleAsync(PayOrderRequest request, CancellationToken ct)
    {
        var buyerId = _httpContextAccessor.HttpContext?.User.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var hasCard = request.Card is not null;
        var hasSavedCard = request.PaymentMethodId is not null;
        if (hasCard == hasSavedCard)
        {
            return Results.BadRequest(new { message = "Provide exactly one of 'card' or 'paymentMethodId'." });
        }

        try
        {
            var order = hasCard
                ? await _paymentService.PayWithCardAsync(buyerId, request.OrderId, request.Card!.ToCardDetails(), ct)
                : await _paymentService.PayWithSavedCardAsync(buyerId, request.OrderId, request.PaymentMethodId!.Value, ct);

            if (order is null)
            {
                return Results.NotFound(new { message = $"Order {request.OrderId} was not found." });
            }

            var response = new PayOrderResponse(request.CorrelationId())
            {
                OrderId = order.Id,
                Status = order.Status.ToString(),
                AuthorizationId = order.AuthorizationId,
                AuthorizationStatus = order.AuthorizationStatus,
                AuthorizationExpiresAt = order.AuthorizationExpiresAt,
                Amount = order.Total(),
                Currency = order.Currency
            };
            return Results.Ok(response);
        }
        catch (PaymentGatewayException ex)
        {
            return PaymentErrorMapper.ToErrorResult(ex);
        }
        catch (InvalidOperationException ex)
        {
            return Results.Conflict(new { message = ex.Message });
        }
    }
}
