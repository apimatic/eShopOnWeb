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

public class ListContactNumbersEndpoint : IEndpoint<IResult, ListContactNumbersRequest, IContactNumberService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ListContactNumbersEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IContactNumberService service) =>
            {
                return await HandleAsync(new ListContactNumbersRequest(), service);
            })
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(ListContactNumbersRequest request, IContactNumberService service)
    {
        var buyerId = _httpContextAccessor.HttpContext!.GetBuyerId();
        var numbers = await service.ListForBuyerAsync(buyerId);
        var response = new ListContactNumbersResponse(request.CorrelationId());
        response.ContactNumbers.AddRange(numbers.Select(n => new ContactNumberDto
        {
            ContactNumberId = n.Id,
            PhoneNumber = n.CanonicalPhoneNumber,
            CreatedAt = n.CreatedAt
        }));
        return Results.Ok(response);
    }
}
