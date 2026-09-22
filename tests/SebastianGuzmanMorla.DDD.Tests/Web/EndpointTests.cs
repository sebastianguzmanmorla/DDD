using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SebastianGuzmanMorla.DDD.Domain.Interfaces;
using SebastianGuzmanMorla.DDD.Domain.Messaging;
using SebastianGuzmanMorla.DDD.Extensions;
using SebastianGuzmanMorla.DDD.Interfaces;

namespace SebastianGuzmanMorla.DDD.Tests.Web;

public sealed class EndpointTests
{
    [Theory]
    [InlineData(RequestMethod.Get)]
    [InlineData(RequestMethod.Delete)]
    [InlineData(RequestMethod.Post)]
    [InlineData(RequestMethod.Put)]
    [InlineData(RequestMethod.Patch)]
    public async Task MapRequest_BindsInputAndPreservesHandlerStatus(RequestMethod method)
    {
        await using WebApplication app = CreateApp();
        app.MapRequest<EchoRequest, Response>(method, "/echo", "Echo");
        await app.StartAsync();
        using HttpClient client = app.GetTestClient();
        var fromQuery = method is RequestMethod.Get or RequestMethod.Delete;
        using var message = new HttpRequestMessage(new HttpMethod(method.ToString().ToUpperInvariant()), fromQuery ? "/echo?value=hello" : "/echo");
        if (!fromQuery) message.Content = JsonContent.Create(new { Value = "hello" });
        using HttpResponseMessage result = await client.SendAsync(message);
        Assert.Equal(HttpStatusCode.Accepted, result.StatusCode);
        Assert.Equal("hello", (await result.Content.ReadFromJsonAsync<Response>())!.Message);
    }

    [Fact]
    public async Task InvalidJson_Returns400WithoutInvokingHandler()
    {
        await using WebApplication app = CreateApp();
        app.MapRequest<EchoRequest, Response>(RequestMethod.Post, "/echo", "Echo");
        await app.StartAsync();
        using HttpClient client = app.GetTestClient();
        using HttpResponseMessage result = await client.PostAsync("/echo", new StringContent("{", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal(0, app.Services.GetRequiredService<EchoHandler>().Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CustomBinder_UsesGroupPrefixAndShortCircuitsOnBindingError(bool fails)
    {
        var binder = new EchoBinder(fails);
        await using WebApplication app = CreateApp(services => services.AddSingleton<IRequestBinder<EchoRequest, Response>>(binder));
        app.MapGroup("/api").MapRequest<EchoRequest, Response, Response>(RequestMethod.Post, "/api", "/echo", "Echo");
        await app.StartAsync();
        using HttpClient client = app.GetTestClient();
        using HttpResponseMessage result = await client.PostAsync("/api/echo", null);
        Assert.Equal(fails ? HttpStatusCode.UnprocessableEntity : HttpStatusCode.Accepted, result.StatusCode);
        Assert.Equal(fails ? "Invalid input" : "bound", (await result.Content.ReadFromJsonAsync<Response>())!.Message);
        Assert.Equal(fails ? 0 : 1, app.Services.GetRequiredService<EchoHandler>().Calls);
        Assert.True(binder.Called);
    }

    [Fact]
    public async Task FileBytes_PreservesContentTypeDownloadNameAndPayload()
    {
        await using WebApplication app = CreateApp();
        app.MapGet("/file", () => Task.FromResult(new ResponseFileByte
        { Bytes = [1, 2, 3], FileName = "report.bin", FileType = "application/octet-stream" }).HandleResponse());
        await app.StartAsync();
        using HttpClient client = app.GetTestClient();
        using HttpResponseMessage result = await client.GetAsync("/file");
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal("application/octet-stream", result.Content.Headers.ContentType!.MediaType);
        Assert.Equal("report.bin", result.Content.Headers.ContentDisposition!.FileNameStar);
        Assert.Equal(new byte[] { 1, 2, 3 }, await result.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task FilePath_ReturnsDiskContents()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "export");
            await using WebApplication app = CreateApp();
            app.MapGet("/file", () => Task.FromResult(new ResponseFilePath { FilePath = path, FileName = "export.txt", FileType = "text/plain" }).HandleResponse());
            await app.StartAsync();
            using HttpClient client = app.GetTestClient();
            using HttpResponseMessage result = await client.GetAsync("/file");
            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            Assert.Equal("export", await result.Content.ReadAsStringAsync());
            Assert.Equal("export.txt", result.Content.Headers.ContentDisposition!.FileNameStar);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task FailedFileResponse_ReturnsJsonWithoutFileContents()
    {
        await using WebApplication app = CreateApp();
        app.MapGet("/file", () => Task.FromResult(new ResponseFileByte
        { Status = HttpStatusCode.NotFound, Message = "Missing export", Bytes = [1, 2, 3] }).HandleResponse());
        await app.StartAsync();
        using HttpClient client = app.GetTestClient();
        using HttpResponseMessage result = await client.GetAsync("/file");
        Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
        Assert.Equal("application/json", result.Content.Headers.ContentType!.MediaType);
        var json = await result.Content.ReadAsStringAsync();
        Assert.Contains("Missing export", json);
        Assert.DoesNotContain("bytes", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Redirect_UsesResponseLocation()
    {
        await using WebApplication app = CreateApp();
        app.MapGet("/redirect", () => Task.FromResult(new Response { Status = HttpStatusCode.Redirect, Message = "/destination" }).HandleResponse());
        await app.StartAsync();
        using HttpClient client = app.GetTestClient();
        using HttpResponseMessage response = await client.GetAsync("/redirect");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/destination", response.Headers.Location!.OriginalString);
    }

    private static WebApplication CreateApp(Action<IServiceCollection>? configure = null)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<EchoHandler>();
        builder.Services.AddSingleton<IRequestHandler<EchoRequest, Response>>(sp => sp.GetRequiredService<EchoHandler>());
        configure?.Invoke(builder.Services);
        return builder.Build();
    }

    public sealed class EchoRequest : Request<Response>
    {
        public string? Value { get; set; }
        public override void ClearSensitiveProperties() { }
    }

    private sealed class EchoHandler : IRequestHandler<EchoRequest, Response>
    {
        public int Calls { get; private set; }
        public Task<Response> Handle(EchoRequest request, CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult(new Response { Status = HttpStatusCode.Accepted, Message = request.Value! }); }
    }

    private sealed class EchoBinder(bool fails) : IRequestBinder<EchoRequest, Response>
    {
        public bool Called { get; private set; }
        public Task<(EchoRequest?, Response?)> BindAsync(CancellationToken cancellationToken = default)
        {
            Called = true;
            return Task.FromResult<(EchoRequest?, Response?)>(fails
                ? (null, new Response { Status = HttpStatusCode.UnprocessableEntity, Message = "Invalid input" })
                : (new EchoRequest { Value = "bound" }, null));
        }
    }
}
