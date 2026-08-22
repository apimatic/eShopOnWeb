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
            async (HttpContext httpContext, IContactNumberService contactNumberService) =>
            {
                var buyerId = httpContext.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                var numbers = await contactNumberService.ListForBuyerAsync(buyerId);
                var response = new ListContactNumbersResponse
                {
                    ContactNumbers = numbers.Select(c => new ContactNumberDto
                    {
                        ContactNumberId = c.Id,
                        PhoneNumber = c.PhoneNumber,
                        NationalFormat = c.NationalFormat,
                        CountryCode = c.CountryCode
                    }).ToList()
                };
                return Results.Ok(response);
            })
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(IContactNumberService contactNumberService)
    {
        return Task.FromResult<IResult>(Results.StatusCode(StatusCodes.Status501NotImplemented));
    }
}

public class ContactNumberDto
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public string? NationalFormat { get; set; }
    public string? CountryCode { get; set; }
}

public class ListContactNumbersResponse : BaseResponse
{
    public List<ContactNumberDto> ContactNumbers { get; set; } = new();
}
