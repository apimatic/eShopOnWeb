using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Authorizes (holds) an order's total with PayPal - either with a one-off card, or a saved card. Does
/// not take the money; see FulfilOrderEndpoint for the capture.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (int orderId, PayOrderRequest request, ClaimsPrincipal user, IOrderPaymentService orderPaymentService) =>
            {
                request.OrderId = orderId;
                request.BuyerId = user.Identity!.Name!;
                return await HandleAsync(request, orderPaymentService);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService orderPaymentService)
    {
        var response = new PayOrderResponse(request.CorrelationId());

        var card = request.Card is null
            ? null
            : new CardDetails(
                request.Card.Number,
                request.Card.ExpiryYearMonth,
                request.Card.SecurityCode,
                request.Card.CardholderName,
                request.Card.AddressLine1,
                request.Card.AddressLine2,
                request.Card.City,
                request.Card.State,
                request.Card.PostalCode,
                request.Card.CountryCode);

        var order = await orderPaymentService.AuthorizePaymentAsync(request.OrderId, request.BuyerId, card, request.SavedPaymentMethodId);
        if (order is null)
        {
            return Results.NotFound();
        }

        response.OrderId = order.Id;
        response.Order = order.ToDto();
        return Results.Ok(response);
    }
}
