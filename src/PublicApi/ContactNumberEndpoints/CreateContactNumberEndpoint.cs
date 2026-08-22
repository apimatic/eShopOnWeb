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

public class CreateContactNumberEndpoint : IEndpoint<IResult, CreateContactNumberRequest, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateContactNumberRequest request, ClaimsPrincipal user, IContactNumberService contactNumbers) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(request, buyerId, contactNumbers);
            })
            .Produces<CreateContactNumberResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(CreateContactNumberRequest request, IContactNumberService contactNumbers)
        => HandleAsync(request, string.Empty, contactNumbers);

    private async Task<IResult> HandleAsync(CreateContactNumberRequest request, string buyerId, IContactNumberService contactNumbers)
    {
        var created = await contactNumbers.RegisterAsync(buyerId, request.PhoneNumber);
        var response = new CreateContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = created.Id
        };
        return Results.Created($"api/contact-numbers/{created.Id}", response);
    }
}
