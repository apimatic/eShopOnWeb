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

public class ListContactNumbersResponse : BaseResponse
{
    public ListContactNumberDto[] ContactNumbers { get; set; } = System.Array.Empty<ListContactNumberDto>();
}

public class ListContactNumberDto
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
}

public class ListContactNumbersEndpoint : IEndpoint<IResult, HttpContext, IShopperContactService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext http, IShopperContactService contacts) =>
            {
                return await HandleAsync(http, contacts);
            })
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext http, IShopperContactService contacts)
    {
        var buyerId = http.User.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var numbers = await contacts.ListAsync(buyerId, http.RequestAborted);
        var response = new ListContactNumbersResponse
        {
            ContactNumbers = numbers.Select(n => new ListContactNumberDto
            {
                ContactNumberId = n.Id,
                PhoneNumber = n.PhoneNumber
            }).ToArray()
        };
        return Results.Ok(response);
    }
}
