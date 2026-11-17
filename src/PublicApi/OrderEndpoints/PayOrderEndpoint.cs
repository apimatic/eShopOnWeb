using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Authorize (hold) the order total using either one-off card details or a saved card.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, PaymentService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public PayOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, PaymentService paymentService) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, paymentService);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, PaymentService paymentService)
    {
        try
        {
            var ownerId = EndpointHelpers.GetOwnerId(_httpContextAccessor.HttpContext!.User);
            var command = new PayOrderCommand(request.Card?.ToCardDetails(), request.PaymentMethodId);
            var payment = await paymentService.PayOrderAsync(ownerId, request.OrderId, command);

            return Results.Ok(new PayOrderResponse(request.CorrelationId())
            {
                OrderId = payment.OrderId,
                Order = OrderPaymentDto.From(payment)
            });
        }
        catch (PaymentOperationException ex)
        {
            return EndpointHelpers.ToResult(ex);
        }
    }
}
