using System.Security.Claims;
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

/// <summary>The caller's own orders, each with the state of its money.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, MyOrdersRequest, IPaymentProcessingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal caller, IPaymentProcessingService payments) =>
            {
                // A GET has no body, so the caller is taken from the token and put on the request here.
                return await HandleAsync(new MyOrdersRequest { Actor = RequestActor.From(caller) }, payments);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(MyOrdersRequest request, IPaymentProcessingService payments)
    {
        var actor = request.RequireActor();
        var response = new MyOrdersResponse(request.CorrelationId());

        var orders = await payments.GetOrdersForBuyerAsync(actor.BuyerId);
        foreach (var order in orders)
        {
            response.Orders.Add(PlacedOrderDto.From(order.Order, order.Payment));
        }

        return Results.Ok(response);
    }
}
