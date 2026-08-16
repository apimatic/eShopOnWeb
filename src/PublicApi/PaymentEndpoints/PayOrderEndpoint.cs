using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class PayOrderRequest : BaseRequest
{
    public int OrderId { get; set; }

    /// <summary>Card details for a one-off payment. Provide this OR <see cref="SavedPaymentMethodId"/>.</summary>
    public CardRequest? Card { get; set; }

    /// <summary>Id of one of the caller's saved cards to pay with. Provide this OR <see cref="Card"/>.</summary>
    public int? SavedPaymentMethodId { get; set; }
}

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(Guid correlationId) : base(correlationId) { }
    public PayOrderResponse() { }

    public PaymentStateDto? Payment { get; set; }
}

/// <summary>
/// POST /api/orders/{orderId}/pay — authorizes (holds) the order total. Money is held, not taken.
/// Shopper-scoped; acts only on the caller's own order.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IPaymentService>
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
            (int orderId, PayOrderRequest request, IPaymentService paymentService) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, paymentService);
            })
            .Produces<PayOrderResponse>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IPaymentService paymentService)
    {
        var buyerId = _httpContextAccessor.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

        var card = request.Card is null ? null : PaymentMapper.ToPaymentCard(request.Card);

        try
        {
            var payment = await paymentService.AuthorizeAsync(request.OrderId, buyerId, card, request.SavedPaymentMethodId);
            var response = new PayOrderResponse(request.CorrelationId())
            {
                Payment = PaymentMapper.ToStateDto(payment)
            };
            return Results.Ok(response);
        }
        catch (PaymentNotFoundException ex)
        {
            return PaymentResults.NotFound(ex);
        }
        catch (PaymentException ex)
        {
            return PaymentResults.FromException(ex);
        }
    }
}
