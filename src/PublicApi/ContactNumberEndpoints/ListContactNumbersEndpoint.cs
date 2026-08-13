using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Configuration;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class ListContactNumbersResponse : BaseResponse
{
    public List<ContactNumberDto> ContactNumbers { get; set; } = new();
}

/// <summary>Lists the signed-in shopper's own registered contact numbers.</summary>
public class ListContactNumbersEndpoint : IEndpoint<IResult, IContactNumberService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ListContactNumbersEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IContactNumberService service) =>
                await HandleAsync(service))
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(IContactNumberService service)
    {
        var ownerId = _httpContextAccessor.GetOwnerId();
        if (string.IsNullOrEmpty(ownerId))
        {
            return Results.Unauthorized();
        }

        var numbers = await service.ListAsync(ownerId, _httpContextAccessor.RequestAborted());
        var response = new ListContactNumbersResponse
        {
            ContactNumbers = numbers.Select(n => n.ToDto()).ToList()
        };

        return Results.Ok(response);
    }
}
