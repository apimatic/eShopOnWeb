using System.Linq;
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

public class GetMyOrdersEndpoint : IEndpoint<IResult, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IOrderPaymentService payments, ClaimsPrincipal user) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(payments, buyerId);
            })
            .Produces<GetMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(IOrderPaymentService payments) =>
        Task.FromResult<IResult>(Results.Unauthorized());

    private async Task<IResult> HandleAsync(IOrderPaymentService payments, string buyerId)
    {
        var orders = await payments.ListMyOrdersAsync(buyerId, default);
        return Results.Ok(new GetMyOrdersResponse
        {
            Orders = orders.Select(OrderPaymentDto.From).ToList()
        });
    }
}

public class GetMyOrdersResponse : BaseResponse
{
    public System.Collections.Generic.List<OrderPaymentDto> Orders { get; set; } = new();
}
