using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class ListPaymentMethodsEndpoint : IEndpoint<IResult, string, ISavedPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISavedPaymentMethodService service, CancellationToken ct) =>
            {
                var methods = await service.ListAsync(user.GetBuyerId(), ct);
                return Results.Ok(methods.Select(PaymentMethodResponse.From).ToList());
            })
            .Produces<System.Collections.Generic.List<PaymentMethodResponse>>()
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(string request, ISavedPaymentMethodService service) =>
        Task.FromResult(Results.StatusCode(StatusCodes.Status501NotImplemented));
}
