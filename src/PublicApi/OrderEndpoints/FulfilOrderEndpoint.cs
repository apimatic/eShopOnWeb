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
/// Operator action: capture the held funds and mark the order fulfilled.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, int, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService paymentService) =>
            {
                return await HandleAsync(orderId, paymentService);
            })
            .Produces<FulfilOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IOrderPaymentService paymentService)
    {
        var order = await paymentService.FulfilOrderAsync(orderId);

        var response = new FulfilOrderResponse
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            CapturedAmountCents = order.CapturedAmountCents,
            FinixFeeCents = order.FinixFeeCents,
            NetProceedsCents = order.NetProceedsCents,
            Payment = OrderResponseMapper.ToPaymentDto(order),
            Order = OrderResponseMapper.ToDto(order)
        };

        return Results.Ok(response);
    }
}
