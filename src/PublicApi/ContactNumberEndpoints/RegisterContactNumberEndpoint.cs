using System.Text.Json.Serialization;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class RegisterContactNumberRequest : BaseRequest
{
    /// <summary>The mobile number as typed. Validated and canonicalised by the provider.</summary>
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>Optional two-letter ISO country code, used when <see cref="PhoneNumber"/> is in national format.</summary>
    public string? CountryCode { get; set; }

    [JsonIgnore]
    public string? CallerId { get; set; }
}

public class RegisterContactNumberResponse : BaseResponse
{
    public RegisterContactNumberResponse(System.Guid correlationId) : base(correlationId) { }
    public RegisterContactNumberResponse() { }

    /// <summary>Identifier of the registered number (top-level, so the flow can be driven end to end).</summary>
    public int ContactNumberId { get; set; }

    /// <summary>The stored canonical number, masked for display.</summary>
    public string PhoneNumber { get; set; } = string.Empty;
}

/// <summary>
/// POST /api/contact-numbers — register a mobile number for the signed-in shopper.
/// </summary>
public class RegisterContactNumberEndpoint : IEndpoint<IResult, RegisterContactNumberRequest, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, ClaimsPrincipal user, IContactNumberService service) =>
            {
                request.CallerId = user.Identity?.Name;
                return await HandleAsync(request, service);
            })
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(RegisterContactNumberRequest request, IContactNumberService service)
    {
        if (string.IsNullOrEmpty(request.CallerId))
            return Results.Unauthorized();

        var result = await service.RegisterAsync(request.CallerId!, request.PhoneNumber, request.CountryCode, CancellationToken.None);
        if (!result.Success)
            return Results.BadRequest(new { message = "The number was rejected by the provider.", errors = result.Errors });

        var response = new RegisterContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = result.ContactNumber!.Id,
            PhoneNumber = PhoneMask.Mask(result.ContactNumber.PhoneNumber)
        };
        return Results.Created($"api/contact-numbers/{response.ContactNumberId}", response);
    }
}
