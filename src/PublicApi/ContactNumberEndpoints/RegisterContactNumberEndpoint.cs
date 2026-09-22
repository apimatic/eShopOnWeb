using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Twilio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Registers a mobile number for the signed-in shopper. The number is validated with the provider and its
/// canonical form is stored; an unusable destination is rejected here.
/// </summary>
public class RegisterContactNumberEndpoint
    : IEndpoint<IResult, RegisterContactNumberRequest, IShopperContactService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, ClaimsPrincipal user, IShopperContactService service,
                CancellationToken ct) =>
            {
                request.BuyerId = user.Identity?.Name; // identity comes from the token, never the body
                return await HandleAsync(request, service, ct);
            })
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(RegisterContactNumberRequest request, IShopperContactService service,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return Results.BadRequest(new { error = "A phone number is required." });
        }

        try
        {
            var result = await service.RegisterAsync(request.BuyerId!, request.PhoneNumber!, ct);
            if (!result.IsValidNumber)
            {
                return Results.BadRequest(new { error = "The number is not a usable destination." });
            }

            var response = new RegisterContactNumberResponse
            {
                ContactNumberId = result.ContactNumberId,
                PhoneNumber = result.CanonicalNumber!
            };
            return Results.Created($"api/contact-numbers/{result.ContactNumberId}", response);
        }
        catch (TwilioProviderException)
        {
            // The provider could not be reached to validate the number — cannot register it right now.
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }
}

public class RegisterContactNumberRequest : BaseRequest
{
    public string? PhoneNumber { get; set; }

    /// <summary>Set from the caller's token, never bound from the request body.</summary>
    public string? BuyerId { get; set; }
}

public class RegisterContactNumberResponse : BaseResponse
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
}
