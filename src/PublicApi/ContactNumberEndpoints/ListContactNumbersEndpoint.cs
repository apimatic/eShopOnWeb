using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Auth;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class ListContactNumbersEndpoint : IEndpoint<IResult, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IContactNumberService contactNumbers) =>
            {
                return await HandleAsync(user, contactNumbers);
            })
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(IContactNumberService contactNumbers)
        => HandleAsync(new ClaimsPrincipal(), contactNumbers);

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, IContactNumberService contactNumbers)
    {
        var numbers = await contactNumbers.ListForBuyerAsync(HttpUser.GetBuyerId(user));
        var response = new ListContactNumbersResponse
        {
            ContactNumbers = numbers.Select(n => new ContactNumberDto
            {
                ContactNumberId = n.Id,
                PhoneNumber = n.CanonicalNumber
            }).ToList()
        };
        return Results.Ok(response);
    }
}
