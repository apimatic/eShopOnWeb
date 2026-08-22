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

public class ListContactNumbersEndpoint : IEndpoint<IResult, string, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext httpContext, IContactNumberService contactNumbers) =>
            {
                return await HandleAsync(httpContext.User.GetRequiredBuyerId(), contactNumbers);
            })
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(string buyerId, IContactNumberService contactNumbers)
    {
        var numbers = await contactNumbers.ListForBuyerAsync(buyerId);
        var response = new ListContactNumbersResponse
        {
            ContactNumbers = numbers.Select(number => new ContactNumberDto
            {
                ContactNumberId = number.Id,
                PhoneNumber = number.PhoneNumber,
                CreatedAt = number.CreatedAt
            }).ToList()
        };
        return Results.Ok(response);
    }
}

public class ListContactNumbersResponse
{
    public System.Collections.Generic.List<ContactNumberDto> ContactNumbers { get; set; } = new();
}

public class ContactNumberDto
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public System.DateTimeOffset CreatedAt { get; set; }
}
