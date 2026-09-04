using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Shared;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Authorizes the order total with the payment provider, placing a hold on the funds.
/// The request either carries card details or names one of the shopper's saved cards.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public PayOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, IOrderPaymentService paymentService) =>
            {
                return await HandleAsync(new PayOrderRequest(request) { OrderId = orderId }, paymentService);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService paymentService)
    {
        var response = new PayOrderResponse(request.CorrelationId());
        var buyerId = CallerIdentity.Get(_httpContextAccessor.HttpContext);
        var ct = _httpContextAccessor.HttpContext?.RequestAborted ?? System.Threading.CancellationToken.None;

        if (request.Card is not null && request.PaymentMethodId.HasValue)
        {
            throw new InvalidOrderStateException("Provide either card details or a paymentMethodId, not both.", 400);
        }

        var payment = new OrderPaymentMethod(
            request.Card is null ? null : CardPaymentMapper.ToPayPalCardDetails(request.Card),
            request.PaymentMethodId);

        var order = await paymentService.PayAsync(buyerId, request.OrderId, payment, ct);

        response.OrderId = order.Id;
        response.Status = order.Status.ToString();
        response.PayPalOrderId = order.PayPalOrderId;
        response.AuthorizationId = order.AuthorizationId;
        response.AuthorizationStatus = order.AuthorizationStatus;

        return Results.Ok(response);
    }
}