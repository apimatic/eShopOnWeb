using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentGateway;
using Microsoft.eShopWeb.PublicApi;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, IOrderPaymentService service, HttpContext httpContext) =>
            {
                var buyerId = BuyerIdentity.GetBuyerId(httpContext);
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                request.OrderId = orderId;
                request.BuyerId = buyerId;
                return await HandleAsync(request, service);
            })
            .Produces<OrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService service)
    {
        CardDetails? card = null;
        if (request.Card is not null)
        {
            BillingAddress? billing = null;
            if (request.Card.BillingAddress is not null)
            {
                var a = request.Card.BillingAddress;
                billing = new BillingAddress(a.AddressLine1, a.AddressLine2, a.AdminArea2, a.AdminArea1, a.PostalCode, a.CountryCode);
            }

            card = new CardDetails(request.Card.Name, request.Card.Number, request.Card.Expiry, request.Card.SecurityCode, billing);
        }

        var order = await service.AuthorizeAsync(request.OrderId, request.BuyerId, card, request.PaymentMethodId);
        return Results.Ok(OrderResponseMapper.ToResponse(order, request.CorrelationId()));
    }
}
