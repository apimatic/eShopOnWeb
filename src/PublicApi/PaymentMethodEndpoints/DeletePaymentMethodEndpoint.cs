using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodEndpoint.Request, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
                async (string paymentMethodId, [Microsoft.AspNetCore.Mvc.FromServices] IPaymentMethodService service) =>
                    await HandleAsync(new Request { PaymentMethodId = paymentMethodId }, service))
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(Request request, IPaymentMethodService service)
    {
        await service.DeleteAsync(request.PaymentMethodId, default);
        return Results.NoContent();
    }

    public class Request
    {
        public string PaymentMethodId { get; set; } = string.Empty;
    }
}
