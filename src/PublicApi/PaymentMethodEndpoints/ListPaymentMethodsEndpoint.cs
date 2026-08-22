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
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISavedPaymentMethodService saved, ClaimsPrincipal user) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                var methods = await saved.ListAsync(buyerId, default);
                return Results.Ok(new ListPaymentMethodsResponse
                {
                    PaymentMethods = methods.Select(PaymentMethodDto.From).ToList()
                });
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(ISavedPaymentMethodService saved) =>
        Task.FromResult<IResult>(Results.Unauthorized());
}

public class ListPaymentMethodsResponse : BaseResponse
{
    public System.Collections.Generic.List<PaymentMethodDto> PaymentMethods { get; set; } = new();
}
