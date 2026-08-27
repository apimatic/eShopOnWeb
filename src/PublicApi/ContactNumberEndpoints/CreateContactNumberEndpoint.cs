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

/// <summary>
/// Registers a mobile number for the signed-in shopper. The number is validated with the
/// messaging provider and stored in the provider's canonical form.
/// </summary>
public class CreateContactNumberEndpoint : IEndpoint<IResult, CreateContactNumberRequest, ClaimsPrincipal, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateContactNumberRequest request, ClaimsPrincipal claimsPrincipal, IContactNumberService contactNumberService) =>
            {
                return await HandleAsync(request, claimsPrincipal, contactNumberService);
            })
            .Produces<CreateContactNumberResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateContactNumberRequest request, ClaimsPrincipal claimsPrincipal, IContactNumberService contactNumberService)
    {
        var buyerId = claimsPrincipal.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return Results.BadRequest(new { error = "A phone number is required." });
        }

        var result = await contactNumberService.RegisterAsync(buyerId, request.PhoneNumber, request.CountryCode);
        if (!result.Succeeded)
        {
            return Results.BadRequest(new { error = result.Error });
        }

        var response = new CreateContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = result.ContactNumber!.Id,
            PhoneNumber = result.ContactNumber.PhoneNumber,
            NationalFormat = result.ContactNumber.NationalFormat
        };

        return Results.Created($"api/contact-numbers/{response.ContactNumberId}", response);
    }
}
