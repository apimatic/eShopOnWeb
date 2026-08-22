using System.Collections.Generic;
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
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IContactNumberService service, HttpContext httpContext) =>
            {
                return await HandleAsync(service, httpContext);
            })
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(IContactNumberService contactNumberService)
        => HandleAsync(contactNumberService, null!);

    private Task<IResult> HandleAsync(IContactNumberService service, HttpContext httpContext)
    {
        return EndpointHelpers.ExecuteAsync(async () =>
        {
            var buyerId = httpContext.User.RequireBuyerId();
            var numbers = await service.ListForBuyerAsync(buyerId);
            var response = new ListContactNumbersResponse
            {
                ContactNumbers = numbers.Select(n => new ContactNumberDto
                {
                    ContactNumberId = n.Id,
                    PhoneNumber = n.PhoneNumber,
                    NationalFormat = n.NationalFormat,
                    CountryCode = n.CountryCode
                }).ToList()
            };
            return Results.Ok(response);
        });
    }
}

public class ListContactNumbersResponse
{
    public List<ContactNumberDto> ContactNumbers { get; set; } = new();
}

public class ContactNumberDto
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public string? NationalFormat { get; set; }
    public string? CountryCode { get; set; }
}
