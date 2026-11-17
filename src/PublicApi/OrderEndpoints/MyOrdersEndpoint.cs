using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class MyOrdersEndpoint : IEndpoint<IResult, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
                async ([Microsoft.AspNetCore.Mvc.FromServices] IOrderPaymentService service) => await HandleAsync(service))
            .Produces<IReadOnlyList<MyOrderResponse>>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(IOrderPaymentService service)
    {
        var response = await service.GetMyOrdersAsync(default);
        return Results.Ok(response);
    }
}
