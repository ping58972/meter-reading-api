using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Meter.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Meter.Tests;

public sealed class ApiTests : IAsyncLifetime
{
    private const string Writer = "test-only-writer-key-0000000000000000";
    private const string Reader = "test-only-reader-key-0000000000000000";
    private readonly string directory = Path.Combine(Path.GetTempPath(), "meter-api-" + Guid.NewGuid().ToString("N"));
    private WebApplicationFactory<Program> factory = null!;

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(directory);
        factory = new Factory(Path.Combine(directory, "api.db"));
        return Task.CompletedTask;
    }

    private sealed class Factory(string file) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("MeterWriterKey", Writer);
            builder.UseSetting("MeterReaderKey", Reader);
            builder.UseSetting("MeterDbPath", file);
        }
    }

    private HttpClient Client(string? key = Writer)
    {
        var client = factory.CreateClient();
        if (key is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return client;
    }

    [Fact] public async Task HttpCreateReplayReadAndEventContract()
    {
        using var writer = Client();
        var request = new ReadingRequest(Guid.NewGuid().ToString("D"), "MTR-001", 100);
        using var created = await writer.PostAsJsonAsync("/readings", request);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var submission = await created.Content.ReadFromJsonAsync<Submission>();
        Assert.False(submission!.Replay);
        using var replay = await writer.PostAsJsonAsync("/readings", request);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.True((await replay.Content.ReadFromJsonAsync<Submission>())!.Replay);
        Assert.Equal(100, (await writer.GetFromJsonAsync<Reading>("/meters/MTR-001/latest"))!.ValueWh);
        using var reader = Client(Reader);
        Assert.Single((await reader.GetFromJsonAsync<IntegrationEvent[]>("/events"))!);
        Assert.Contains("no-store", created.Headers.CacheControl!.ToString());
    }

    [Theory] [InlineData(null)] [InlineData("invalid")]
    public async Task RejectUnauthenticatedWrites(string? key)
    {
        using var client = Client(key);
        using var response = await client.PostAsJsonAsync("/readings", new ReadingRequest(Guid.NewGuid().ToString(), "MTR-001", 1));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact] public async Task EnforceRoles()
    {
        using var reader = Client(Reader);
        using var response = await reader.PostAsJsonAsync("/readings", new ReadingRequest(Guid.NewGuid().ToString(), "MTR-001", 1));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var writer = Client();
        using var events = await writer.GetAsync("/events");
        Assert.Equal(HttpStatusCode.Forbidden, events.StatusCode);
    }

    [Fact] public async Task RejectMalformedInputWithoutWrites()
    {
        using var writer = Client();
        using var malformed = await writer.PostAsync("/readings", new StringContent("{", new MediaTypeHeaderValue("application/json")));
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        using var invalid = await writer.PostAsJsonAsync("/readings", new ReadingRequest("invalid", "MTR-001", -1));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var reader = Client(Reader);
        Assert.Empty((await reader.GetFromJsonAsync<IntegrationEvent[]>("/events"))!);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"operationId\":\"00000000-0000-4000-8000-000000000001\",\"meterId\":\"MTR-001\"}")]
    [InlineData("{\"operationId\":\"00000000-0000-4000-8000-000000000001\",\"meterId\":\"MTR-001\",\"valueWh\":1,\"extra\":1}")]
    [InlineData("{\"operationId\":\"00000000-0000-4000-8000-000000000001\",\"meterId\":\"MTR-001\",\"valueWh\":1,\"valueWh\":2}")]
    public async Task StrictJsonRejectsAmbiguousRequests(string body)
    {
        using var writer = Client();
        using var response = await writer.PostAsync("/readings", new StringContent(body, new MediaTypeHeaderValue("application/json")));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var reader = Client(Reader);
        Assert.Empty((await reader.GetFromJsonAsync<IntegrationEvent[]>("/events"))!);
    }

    public async Task DisposeAsync()
    {
        await factory.DisposeAsync();
        Directory.Delete(directory, recursive: true);
    }
}
