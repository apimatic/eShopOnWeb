using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// An operator calls an order off before fulfilment. Whatever was held is released, so no money ever
/// moves.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, IPaymentProcessingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IPaymentProcessingService payments) =>
            {
                return await HandleAsync(new CancelOrderRequest(orderId), payments);
            })
            .Produces<CancelOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request, IPaymentProcessingService payments)
    {
        var response = new CancelOrderResponse(request.CorrelationId()) { OrderId = request.OrderId };

        var result = await payments.CancelAsync(request.OrderId);

        response.OrderStatus = result.Order.Status.ToString();
        response.AlreadyRecorded = result.AlreadyRecorded;
        response.Payment = result.Payment is null ? null : PaymentDto.From(result.Payment);
        response.Note = result.Note;

        return Results.Ok(response);
    }
}
