using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Pays for an order with PayPal, using either one-off card details or one of the shopper's saved
/// cards. Idempotent in effect: paying an already-paid order returns the existing payment without
/// charging again.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, IOrderPaymentService orderPaymentService,
                CancellationToken ct) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                request.SetRouteAndBuyer(orderId, buyerId);

                if (!request.HasExactlyOnePaymentSource)
                {
                    return Results.BadRequest(new
                    {
                        errors = new[] { "Supply exactly one of 'card' or 'savedPaymentMethodId'." }
                    });
                }

                return await HandleAsync(request, orderPaymentService, ct);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService orderPaymentService,
        CancellationToken ct)
    {
        var buyerId = request.BuyerId!;

        Ardalis.Result.Result<Order> result = request.SavedPaymentMethodId.HasValue
            ? await orderPaymentService.PayWithSavedCardAsync(buyerId, request.OrderId,
                request.SavedPaymentMethodId.Value, ct)
            : await orderPaymentService.PayWithCardAsync(buyerId, request.OrderId,
                request.Card!.ToCardDetails(), ct);

        var failure = ApiResultMapper.MapFailure(result);
        if (failure is not null)
        {
            return failure;
        }

        var order = result.Value;
        var response = new PayOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            PaymentStatus = order.PaymentStatus.ToString(),
            Order = OrderDto.From(order)
        };
        return Results.Ok(response);
    }
}
