using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class GetMyOrdersEndpoint : IEndpoint<IResult, GetMyOrdersRequest, IPaymentApplicationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext context, IPaymentApplicationService service) =>
            {
                return await HandleAsync(new GetMyOrdersRequest
                {
                    BuyerId = PaymentEndpointUser.GetBuyerId(context)
                }, service);
            })
            .Produces<IReadOnlyList<OrderResponse>>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(GetMyOrdersRequest request, IPaymentApplicationService service)
    {
        var orders = await service.GetMyOrdersAsync(request.BuyerId!);
        return Results.Ok(orders);
    }
}
