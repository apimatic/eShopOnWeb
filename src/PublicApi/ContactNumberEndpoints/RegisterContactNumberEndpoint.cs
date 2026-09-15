using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.PublicApi.Notifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class RegisterContactNumberRequest
{
    /// <summary>The mobile number as the caller typed it. Stored form is the provider's canonical E.164.</summary>
    public string PhoneNumber { get; set; } = string.Empty;

    [JsonIgnore] public string? CallerId { get; set; }
    [JsonIgnore] public CancellationToken Ct { get; set; }
}

/// <summary><see cref="ContactNumberId"/> is the top-level identifier so the flow can be driven end to end.</summary>
public record RegisterContactNumberResponse(int ContactNumberId, string PhoneNumber);

/// <summary>
/// Register a mobile number for the signed-in shopper. A number the provider does not consider a
/// usable destination is rejected here (400), not when a later message fails. What is stored is the
/// provider's canonical form of the number.
/// </summary>
public class RegisterContactNumberEndpoint : IEndpoint<IResult, RegisterContactNumberRequest, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, System.Security.Claims.ClaimsPrincipal user, IContactNumberService service, CancellationToken ct) =>
            {
                var callerId = user.GetCallerId();
                if (callerId is null)
                {
                    return Results.Unauthorized();
                }

                request.CallerId = callerId;
                request.Ct = ct;
                return await HandleAsync(request, service);
            })
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(RegisterContactNumberRequest request, IContactNumberService service)
    {
        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return Results.BadRequest(new { message = "A phone number is required." });
        }

        try
        {
            var result = await service.RegisterAsync(request.CallerId!, request.PhoneNumber, request.Ct);
            if (!result.Accepted)
            {
                return Results.BadRequest(new { message = result.RejectionReason });
            }

            return Results.Created(
                $"api/contact-numbers/{result.ContactNumberId}",
                new RegisterContactNumberResponse(result.ContactNumberId!.Value, result.CanonicalE164!));
        }
        catch (SmsGatewayException ex)
        {
            return SmsErrorResults.ToResult(ex);
        }
    }
}
