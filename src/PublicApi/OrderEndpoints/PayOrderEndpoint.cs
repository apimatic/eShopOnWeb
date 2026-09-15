using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Authorizes (holds) the order total. Does not take the money. The request carries either card
/// details for a one-off payment or the id of one of the shopper's saved cards. Idempotent: a
/// double-click never authorizes twice.
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
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, PayOrderRequest request, IOrderPaymentService service) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, service);
            })
            .Produces<OrderPaymentResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService service)
    {
        var buyerId = _httpContextAccessor.HttpContext!.User.GetBuyerId();

        if (request.PaymentMethodId is null && request.Card is null)
        {
            throw new PaymentOperationException("Provide either 'card' details or a 'paymentMethodId' to pay with.");
        }
        if (request.PaymentMethodId is not null && request.Card is not null)
        {
            throw new PaymentOperationException("Provide either 'card' details or a 'paymentMethodId', not both.");
        }

        var card = request.Card is not null ? PaymentApiMapper.ToCardDetails(request.Card) : null;
        var order = await service.AuthorizeAsync(request.OrderId, buyerId, card, request.PaymentMethodId);
        return Results.Ok(PaymentApiMapper.ToResponse(order));
    }
}
