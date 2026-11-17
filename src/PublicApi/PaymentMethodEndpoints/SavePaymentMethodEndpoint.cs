using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
                async ([Microsoft.AspNetCore.Mvc.FromServices] IPaymentMethodService service, SavePaymentMethodRequest request) =>
                    await HandleAsync(request, service))
            .Produces<PaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, IPaymentMethodService service)
    {
        var response = await service.SaveAsync(request, default);
        return Results.Created($"api/payment-methods/{response.PaymentMethodId}", response);
    }
}
