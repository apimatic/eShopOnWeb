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
/// Operator cancels an order before fulfilment: any held funds are released (voided),
/// so no money ever moves. Fulfilled orders must be refunded instead.
/// </summary>
public class CancelOrderEndpoint : IEndpoint
{
    private readonly IOrderPaymentService _payments;

    public CancelOrderEndpoint(IOrderPaymentService payments)
    {
        _payments = payments;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId) =>
            {
                return await HandleAsync(orderId);
            })
            .Produces<CancelOrderResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId)
    {
        var result = await _payments.CancelAsync(orderId);

        var response = new CancelOrderResponse
        {
            OrderId = result.Order.Id,
            Status = result.Order.Status.ToString(),
            FundsReleased = result.FundsReleased,
            Replayed = result.Replayed
        };

        return Results.Ok(response);
    }
}
