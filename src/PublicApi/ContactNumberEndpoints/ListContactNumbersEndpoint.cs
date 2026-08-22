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

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class ListContactNumbersEndpoint : IEndpoint<IResult, EmptyRequest, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IContactNumberService service, ClaimsPrincipal user) =>
            {
                var unauthorized = HttpCaller.RequireBuyerId(user, out var buyerId);
                if (unauthorized is not null)
                {
                    return unauthorized;
                }

                return await HandleAsync(new EmptyRequest(), service, buyerId);
            })
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(EmptyRequest request, IContactNumberService service)
        => HandleAsync(request, service, string.Empty);

    private async Task<IResult> HandleAsync(EmptyRequest request, IContactNumberService service, string buyerId)
    {
        var response = new ListContactNumbersResponse(request.CorrelationId());
        var numbers = await service.ListForBuyerAsync(buyerId, default);
        response.ContactNumbers.AddRange(numbers.Select(n => new ContactNumberDto
        {
            ContactNumberId = n.Id,
            CanonicalNumber = n.CanonicalNumber,
            CreatedAt = n.CreatedAt
        }));
        return Results.Ok(response);
    }
}
