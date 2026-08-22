using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CancelOrderEndpoint : IEndpoint<IResult, int, IOrderNotificationWorkflow>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IOrderNotificationWorkflow workflow) =>
            {
                return await HandleAsync(orderId, workflow);
            })
            .Produces<OrderLifecycleResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IOrderNotificationWorkflow workflow)
    {
        var result = await workflow.CancelAsync(orderId);
        if (!result.Succeeded || result.Order == null)
        {
            return ApiResults.From(result.StatusCode, error: result.Error);
        }

        return Results.Ok(new OrderLifecycleResponse
        {
            OrderId = result.Order.Id,
            Status = result.Order.Status.ToString()
        });
    }
}
