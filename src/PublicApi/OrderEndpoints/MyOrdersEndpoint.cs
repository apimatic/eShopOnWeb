using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

using Microsoft.eShopWeb.PublicApi;
using Microsoft.eShopWeb.PublicApi.Payments;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class MyOrdersEndpoint : IEndpoint<IResult, MyOrdersRequest, PaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
                async (HttpContext http, PaymentService payments) =>
                {
                    var request = new MyOrdersRequest
                    {
                        BuyerId = http.User.Identity?.Name ?? string.Empty,
                        RequestAborted = http.RequestAborted
                    };
                    return await HandleAsync(request, payments);
                })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(MyOrdersRequest request, PaymentService payments)
    {
        if (string.IsNullOrWhiteSpace(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        var orders = await payments.GetMyOrdersAsync(request.BuyerId, request.RequestAborted);
        return Results.Ok(new MyOrdersResponse { Orders = orders });
    }
}

public class MyOrdersRequest : PaymentRequestBase
{
}

public class MyOrdersResponse : BaseResponse
{
    public IReadOnlyList<OrderPaymentView> Orders { get; set; } = new List<OrderPaymentView>();
}
