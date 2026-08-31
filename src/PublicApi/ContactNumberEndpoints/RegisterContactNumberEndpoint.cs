using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Registers a mobile number for the signed-in shopper. The number is validated with the
/// messaging provider first; the provider's canonical form is what gets stored.
/// </summary>
public class RegisterContactNumberEndpoint : IEndpoint<IResult, RegisterContactNumberRequest, ClaimsPrincipal, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, ClaimsPrincipal user, IContactNumberService contactNumberService) =>
            {
                return await HandleAsync(request, user, contactNumberService);
            })
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(RegisterContactNumberRequest request, ClaimsPrincipal user, IContactNumberService contactNumberService)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }
        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return Results.BadRequest(new { error = "phoneNumber is required." });
        }

        try
        {
            var result = await contactNumberService.RegisterAsync(buyerId, request.PhoneNumber);
            if (result.IsDuplicate)
            {
                return Results.Conflict(new { error = result.Error });
            }
            if (!result.Success)
            {
                return Results.BadRequest(new { error = result.Error });
            }

            var response = new RegisterContactNumberResponse(request.CorrelationId())
            {
                ContactNumberId = result.ContactNumber!.Id,
                PhoneNumber = result.ContactNumber.PhoneNumber
            };
            return Results.Created($"api/contact-numbers/{response.ContactNumberId}", response);
        }
        catch (SmsProviderException ex)
        {
            return ProviderErrorResults.Map(ex);
        }
    }
}
