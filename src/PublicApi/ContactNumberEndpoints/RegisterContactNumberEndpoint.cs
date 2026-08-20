using System.Threading;
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

public class RegisterContactNumberEndpoint : IEndpoint<IResult, RegisterContactNumberRequest, IContactNumberService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public RegisterContactNumberEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, HttpContext httpContext, IContactNumberService contactNumberService) =>
            {
                var buyerId = EndpointIdentity.GetBuyerId(httpContext);
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(request, contactNumberService, buyerId, httpContext.RequestAborted);
            })
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(RegisterContactNumberRequest request, IContactNumberService contactNumberService)
    {
        var http = _httpContextAccessor.HttpContext;
        var buyerId = http is null ? string.Empty : EndpointIdentity.GetBuyerId(http);
        if (string.IsNullOrEmpty(buyerId))
        {
            return Task.FromResult(Results.Unauthorized());
        }

        return HandleAsync(request, contactNumberService, buyerId, http!.RequestAborted);
    }

    private static async Task<IResult> HandleAsync(
        RegisterContactNumberRequest request,
        IContactNumberService contactNumberService,
        string buyerId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return Results.BadRequest(new { message = "PhoneNumber is required." });
        }

        try
        {
            var contact = await contactNumberService.RegisterAsync(buyerId, request.PhoneNumber.Trim(), cancellationToken);
            var response = new RegisterContactNumberResponse(request.CorrelationId())
            {
                ContactNumberId = contact.Id,
                CanonicalNumber = contact.CanonicalNumber
            };
            return Results.Created($"api/contact-numbers/{contact.Id}", response);
        }
        catch (UnusableContactNumberException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
        catch (SmsProviderException)
        {
            return Results.Json(new { message = "The number could not be verified." }, statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
