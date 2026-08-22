using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ISavedPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ISavedPaymentMethodService service, ClaimsPrincipal user) => await HandleAsync(service, user))
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(ISavedPaymentMethodService service) =>
        Task.FromResult(Results.BadRequest());

    private static async Task<IResult> HandleAsync(ISavedPaymentMethodService service, ClaimsPrincipal user)
    {
        var methods = await service.ListAsync(user.GetRequiredUserName());
        return Results.Ok(new ListPaymentMethodsResponse
        {
            PaymentMethods = methods.Select(PaymentMethodDtoMapper.ToDto).ToList()
        });
    }
}
