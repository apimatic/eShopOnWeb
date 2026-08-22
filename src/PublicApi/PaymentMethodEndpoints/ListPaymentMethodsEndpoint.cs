using System.Linq;
using System.Security.Claims;
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

public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ListPaymentMethodsRequest, ISavedCardService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ISavedCardService service, ClaimsPrincipal user) =>
            {
                return await HandleAsync(new ListPaymentMethodsRequest(), service, user);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(ListPaymentMethodsRequest request, ISavedCardService service) =>
        HandleAsync(request, service, new ClaimsPrincipal());

    private async Task<IResult> HandleAsync(ListPaymentMethodsRequest request, ISavedCardService service, ClaimsPrincipal user)
    {
        var buyerId = EndpointUser.RequireBuyerId(user);
        var cards = await service.ListAsync(buyerId);
        return Results.Ok(new ListPaymentMethodsResponse
        {
            PaymentMethods = cards.Select(SavedCardDto.From).ToList()
        });
    }
}

public class ListPaymentMethodsRequest : BaseRequest
{
}

public class ListPaymentMethodsResponse
{
    public System.Collections.Generic.List<SavedCardDto> PaymentMethods { get; set; } = new();
}
