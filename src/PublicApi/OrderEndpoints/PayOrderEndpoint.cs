using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Authorizes (holds) the order total for the caller's own order, paying with supplied card details
/// or one of the caller's saved cards. Does not take the money — that happens at fulfilment.
/// Idempotent: a double-click never authorizes twice.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService>
{
    private readonly IPaymentConfiguration _paymentConfiguration;

    public PayOrderEndpoint(IPaymentConfiguration paymentConfiguration)
    {
        _paymentConfiguration = paymentConfiguration;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, IOrderPaymentService service) =>
            {
                request ??= new PayOrderRequest();
                request.OrderId = orderId;
                request.BuyerId = user.GetBuyerId();
                return await HandleAsync(request, service);
            })
            .Produces<OrderSummaryDto>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService service)
    {
        if (request.Card is null && request.SavedPaymentMethodId is null)
        {
            return Results.BadRequest("Provide card details or a savedPaymentMethodId to pay with.");
        }
        if (request.Card is not null && request.SavedPaymentMethodId is not null)
        {
            return Results.BadRequest("Provide either card details or a savedPaymentMethodId, not both.");
        }

        var order = await service.AuthorizeOrderAsync(request.BuyerId, request.OrderId,
            request.Card?.ToCardDetails(), request.SavedPaymentMethodId);

        return Results.Ok(OrderMapper.ToSummary(order, _paymentConfiguration.Currency));
    }
}
