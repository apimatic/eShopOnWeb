using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class ListContactNumbersEndpoint : IEndpoint<IResult, ListContactNumbersRequest>
{
    private readonly IContactNumberService _contactNumbers;

    public ListContactNumbersEndpoint(IContactNumberService contactNumbers)
    {
        _contactNumbers = contactNumbers;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext) =>
            {
                var unauthorized = HttpCaller.UnauthorizedIfAnonymous(httpContext);
                if (unauthorized is not null)
                {
                    return unauthorized;
                }

                return await HandleAsync(new ListContactNumbersRequest
                {
                    BuyerId = HttpCaller.BuyerId(httpContext)!,
                    CancellationToken = httpContext.RequestAborted
                });
            })
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(ListContactNumbersRequest request)
    {
        var numbers = await _contactNumbers.ListForBuyerAsync(request.BuyerId, request.CancellationToken);
        return Results.Ok(new ListContactNumbersResponse
        {
            ContactNumbers = numbers.Select(n => new ContactNumberDto
            {
                ContactNumberId = n.Id,
                PhoneNumber = n.CanonicalNumber
            }).ToList()
        });
    }
}

public class ListContactNumbersRequest : BaseRequest
{
    internal string BuyerId { get; set; } = string.Empty;
    internal CancellationToken CancellationToken { get; set; }
}

public class ListContactNumbersResponse : BaseResponse
{
    public System.Collections.Generic.List<ContactNumberDto> ContactNumbers { get; set; } = new();
}

public class ContactNumberDto
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
}
