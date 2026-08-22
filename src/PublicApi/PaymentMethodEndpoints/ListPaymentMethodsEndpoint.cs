using System.Linq;
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

public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ISavedCardService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ISavedCardService service, HttpContext http) =>
                await HandleAsync(service, http))
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(ISavedCardService service) =>
        HandleAsync(service, null!);

    private async Task<IResult> HandleAsync(ISavedCardService service, HttpContext http)
    {
        var buyerId = EndpointIdentity.RequireUserName(http);
        var methods = await service.ListAsync(buyerId, http.RequestAborted);
        return Results.Ok(new ListPaymentMethodsResponse
        {
            PaymentMethods = methods.Select(PaymentMethodResponseMapper.From).ToList()
        });
    }
}

public class ListPaymentMethodsResponse
{
    public System.Collections.Generic.List<PaymentMethodResponse> PaymentMethods { get; set; } = new();
}
