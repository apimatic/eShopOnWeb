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
/// An operator marks an order fulfilled, which is when the money that was held is actually taken. A
/// hold that has gone stale is renewed first rather than failing the fulfilment.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest, IPaymentProcessingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IPaymentProcessingService payments) =>
            {
                return await HandleAsync(new FulfilOrderRequest(orderId), payments);
            })
            .Produces<FulfilOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(FulfilOrderRequest request, IPaymentProcessingService payments)
    {
        var response = new FulfilOrderResponse(request.CorrelationId()) { OrderId = request.OrderId };

        var result = await payments.FulfilAsync(request.OrderId);

        response.OrderStatus = result.Order.Status.ToString();
        response.AlreadyRecorded = result.AlreadyRecorded;
        response.RenewedHold = result.RenewedHold;
        response.Payment = result.Payment is null ? null : PaymentDto.From(result.Payment);
        response.Note = result.Note;

        return Results.Ok(response);
    }
}
