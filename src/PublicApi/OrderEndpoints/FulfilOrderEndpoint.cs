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

/// <summary>
/// Operator action: fulfil the order, capturing the held funds. Renews a stale authorization first
/// if needed; if it can no longer be renewed, returns an operator-actionable error.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            [SwaggerOperation(Summary = "Fulfil an order and capture payment (operator)", Tags = new[] { "OrderPaymentEndpoints" })]
            async (int orderId, IPaymentService paymentService, IPaymentConfiguration config, CancellationToken ct) =>
            {
                var state = await paymentService.FulfilAsync(orderId, ct);
                return Results.Ok(PaymentResponseFactory.From(state, config.Currency));
            })
            .Produces<OrderPaymentView>()
            .WithTags("OrderPaymentEndpoints");
    }
}
