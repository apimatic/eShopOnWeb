using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Extensions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>Lists the signed-in shopper's registered numbers (and only theirs).</summary>
public class ListContactNumbersEndpoint : AuthenticatedEndpointBase,
    IEndpoint<IResult, ListContactNumbersRequest, IContactNumberService>
{
    public ListContactNumbersEndpoint(IHttpContextAccessor httpContextAccessor) : base(httpContextAccessor)
    {
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IContactNumberService service) =>
                await HandleAsync(new ListContactNumbersRequest(), service))
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(ListContactNumbersRequest request, IContactNumberService service)
    {
        var numbers = await service.ListAsync(BuyerId, RequestAborted);

        var response = new ListContactNumbersResponse
        {
            ContactNumbers = numbers.Select(n => new ContactNumberDto
            {
                ContactNumberId = n.Id,
                PhoneNumber = n.PhoneNumber,
                RegisteredAt = n.RegisteredAt
            }).ToList()
        };
        return Results.Ok(response);
    }
}
