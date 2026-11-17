using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CardRequest request, IOrderPaymentService paymentService, HttpContext context) =>
            {
                var command = request.ToCommand(EndpointHelpers.GetBuyerId(context));
                var result = await paymentService.SaveCardAsync(command, context.RequestAborted);
                return Results.Created($"api/payment-methods/{result.PaymentMethodId}", result);
            })
            .Produces<SavedPaymentMethodView>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status402PaymentRequired)
            .WithTags("PaymentMethodEndpoints");
    }
}
