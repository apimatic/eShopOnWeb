using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.Infrastructure.Finix;
using Microsoft.eShopWeb.PublicApi.Payments;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class MyOrdersRequest : BaseRequest
{
    [JsonIgnore]
    public string? BuyerId { get; set; }
}

public class MyOrdersResponse
{
    public List<OrderResponse> Orders { get; set; } = new();
}

public class MyOrdersEndpoint : IEndpoint<IResult, MyOrdersRequest, IPaymentService>
{
    private readonly FinixOptions _finixOptions;

    public MyOrdersEndpoint(IOptions<FinixOptions> finixOptions)
    {
        _finixOptions = finixOptions.Value;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IPaymentService paymentService) =>
            {
                return await HandleAsync(new MyOrdersRequest { BuyerId = user.GetBuyerId() }, paymentService);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(MyOrdersRequest request, IPaymentService paymentService)
    {
        var orders = await paymentService.GetOrdersAsync(request.BuyerId!, CancellationToken.None);
        var response = new MyOrdersResponse
        {
            Orders = orders.Select(order => order.ToOrderResponse(_finixOptions.Currency)).ToList()
        };

        return Results.Ok(response);
    }
}
