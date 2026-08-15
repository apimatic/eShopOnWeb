using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public record MyOrdersResponse(IReadOnlyList<OrderPaymentView> Orders);

/// <summary>The caller's orders with their payment state.</summary>
public class MyOrdersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            [SwaggerOperation(Summary = "List the caller's orders with payment state", Tags = new[] { "OrderEndpoints" })]
            async (IPaymentService paymentService, IPaymentConfiguration config, HttpContext http, CancellationToken ct) =>
            {
                var buyerId = http.User.GetBuyerId();
                var states = await paymentService.ListForBuyerAsync(buyerId, ct);
                var views = states.Select(s => PaymentResponseFactory.From(s, config.Currency)).ToList();
                return Results.Ok(new MyOrdersResponse(views));
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }
}
