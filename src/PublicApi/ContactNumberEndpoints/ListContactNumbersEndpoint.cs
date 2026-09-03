using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class ListContactNumbersRequest : BaseRequest
{
}

public class ContactNumberDto
{
    public int ContactNumberId { get; set; }
    public string CanonicalNumber { get; set; } = string.Empty;
}

public class ListContactNumbersResponse : BaseResponse
{
    public ContactNumberDto[] ContactNumbers { get; set; } = [];
}

public class ListContactNumbersEndpoint : IEndpoint<IResult, ListContactNumbersRequest, IShopperContactService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IShopperContactService contacts, HttpContext http) =>
            {
                return await HandleAsync(new ListContactNumbersRequest(), contacts, http);
            })
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(ListContactNumbersRequest request, IShopperContactService contacts)
        => HandleAsync(request, contacts, null!);

    private async Task<IResult> HandleAsync(ListContactNumbersRequest request, IShopperContactService contacts, HttpContext http)
    {
        var numbers = await contacts.ListAsync(http.User.RequireBuyerId(), http.RequestAborted);
        return Results.Ok(new ListContactNumbersResponse
        {
            ContactNumbers = numbers.Select(n => new ContactNumberDto
            {
                ContactNumberId = n.Id,
                CanonicalNumber = n.CanonicalNumber
            }).ToArray()
        });
    }
}
