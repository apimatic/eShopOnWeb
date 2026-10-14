using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;

namespace PublicApiIntegrationTests;

/// <summary>
/// PublicApi references the Maxio SDK, which brings System.Text.Json 10. That serializer requires a response
/// PipeWriter implementing <c>UnflushedBytes</c>; Kestrel's does, but the ASP.NET Core 8 TestServer's
/// ResponseBodyPipeWriter does not, so every JSON result fails with a 500 under WebApplicationFactory.
/// This filter swaps in a stream-backed body writer (which does implement it) for test hosts only.
/// </summary>
public sealed class TestServerResponseBodyFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, nextMiddleware) =>
        {
            var original = context.Features.Get<IHttpResponseBodyFeature>()!;
            var replacement = new StreamResponseBodyFeature(original.Stream, original);
            context.Features.Set<IHttpResponseBodyFeature>(replacement);
            try
            {
                await nextMiddleware(context);
                await replacement.CompleteAsync();
            }
            finally
            {
                context.Features.Set(original);
            }
        });
        next(app);
    };

    public static void AddTo(IWebHostBuilder builder) =>
        builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, TestServerResponseBodyFilter>());
}
