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
/// Operator action: cancel an order before fulfilment, releasing any held funds so no money moves.
/// </summary>
public class CancelOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            [SwaggerOperation(Summary = "Cancel an order and release held funds (operator)", Tags = new[] { "OrderPaymentEndpoints" })]
            async (int orderId, IPaymentService paymentService, IPaymentConfiguration config, CancellationToken ct) =>
            {
                var state = await paymentService.CancelAsync(orderId, ct);
                return Results.Ok(PaymentResponseFactory.From(state, config.Currency));
            })
            .Produces<OrderPaymentView>()
            .WithTags("OrderPaymentEndpoints");
    }
}
