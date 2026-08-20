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

public class ListContactNumbersEndpoint : IEndpoint<IResult, HttpContext, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext http, IContactNumberService contactNumbers) =>
                await HandleAsync(http, contactNumbers))
            .Produces<Response>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext http, IContactNumberService contactNumbers)
    {
        var buyerId = http.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var numbers = await contactNumbers.ListForBuyerAsync(buyerId);
        return Results.Ok(new Response
        {
            ContactNumbers = numbers.Select(n => new Item
            {
                ContactNumberId = n.Id,
                PhoneNumber = n.CanonicalNumber
            }).ToList()
        });
    }

    public class Response
    {
        public System.Collections.Generic.List<Item> ContactNumbers { get; set; } = new();
    }

    public class Item
    {
        public int ContactNumberId { get; set; }
        public string PhoneNumber { get; set; } = string.Empty;
    }
}
