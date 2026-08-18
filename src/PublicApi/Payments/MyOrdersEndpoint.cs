using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>Returns the caller's own orders with their payment state.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, MyOrdersRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, HttpContext http, IPaymentService paymentService) =>
            {
                var request = new MyOrdersRequest { BuyerId = user.GetBuyerId(), Cancellation = http.RequestAborted };
                return await HandleAsync(request, paymentService);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(MyOrdersRequest request, IPaymentService paymentService)
    {
        var orders = await paymentService.GetMyOrdersAsync(request.BuyerId, request.Cancellation);
        var response = new MyOrdersResponse(request.CorrelationId()) { Orders = orders };
        return Results.Ok(response);
    }
}

public class MyOrdersRequest : PaymentRequestBase
{
}

public class MyOrdersResponse : BaseResponse
{
    public MyOrdersResponse(System.Guid correlationId) : base(correlationId) { }
    public MyOrdersResponse() { }

    public IReadOnlyList<OrderPaymentSummary> Orders { get; set; } = new List<OrderPaymentSummary>();
}
