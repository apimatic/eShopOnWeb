using System;
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
            (IContactNumberService contactNumberService) =>
            {
                return await HandleAsync(contactNumberService);
            })
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(IContactNumberService contactNumberService)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("HTTP context is not available.");
        var contacts = await contactNumberService.ListForBuyerAsync(httpContext.GetRequiredBuyerId());
        var response = new ListContactNumbersResponse
        {
            ContactNumbers = contacts.Select(c => new ContactNumberDto
            {
                ContactNumberId = c.Id,
                PhoneNumber = c.PhoneNumberE164,
                NationalFormat = c.NationalFormat
            }).ToList()
        };
        return Results.Ok(response);
    }
}
