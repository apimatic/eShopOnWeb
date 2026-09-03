using System.Linq;
using System.Security.Claims;
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
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IContactNumberService service, ClaimsPrincipal user, HttpContext http) =>
            {
                return await HandleAsync(new ListContactNumbersRequest(), service, user, http.RequestAborted);
            })
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(ListContactNumbersRequest request, IContactNumberService service) =>
        HandleAsync(request, service, new ClaimsPrincipal(), default);

    private async Task<IResult> HandleAsync(
        ListContactNumbersRequest request,
        IContactNumberService service,
        ClaimsPrincipal user,
        System.Threading.CancellationToken cancellationToken)
    {
        var buyerId = user.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var numbers = await service.ListForBuyerAsync(buyerId, cancellationToken);
        var response = new ListContactNumbersResponse(request.CorrelationId());
        response.ContactNumbers.AddRange(numbers.Select(c => new ContactNumberItem
        {
            ContactNumberId = c.Id,
            PhoneNumber = c.CanonicalNumber
        }));
        return Results.Ok(response);
    }
}

public class ListContactNumbersRequest : BaseRequest
{
}
