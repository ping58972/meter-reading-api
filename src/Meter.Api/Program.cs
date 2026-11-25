using Meter.Api;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Data.Sqlite;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 4096);
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.RespectRequiredConstructorParameters = true;
    options.SerializerOptions.AllowDuplicateProperties = false;
    options.SerializerOptions.UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow;
});
builder.Services.AddSingleton(provider =>
{
    var config = provider.GetRequiredService<IConfiguration>();
    return new ApiKeys(config["MeterWriterKey"], config["MeterReaderKey"]);
});
builder.Services.AddSingleton<IReadingStore>(provider =>
    new ReadingStore(provider.GetRequiredService<IConfiguration>()["MeterDbPath"] ?? "meter-readings.db"));
builder.Services.AddAuthentication("key").AddScheme<AuthenticationSchemeOptions, KeyHandler>("key", _ => { });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("write", policy => policy.RequireAuthenticatedUser().RequireRole("writer"));
    options.AddPolicy("read", policy => policy.RequireAuthenticatedUser());
    options.AddPolicy("events", policy => policy.RequireAuthenticatedUser().RequireRole("reader"));
});

var app = builder.Build();
_ = app.Services.GetRequiredService<ApiKeys>();
_ = app.Services.GetRequiredService<IReadingStore>();
app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    try { await next(context); }
    catch (Rejected error)
    {
        context.Response.StatusCode = error.Status;
        await context.Response.WriteAsJsonAsync(new { error = error.Message });
    }
    catch (SqliteException)
    {
        context.Response.StatusCode = 503;
        await context.Response.WriteAsJsonAsync(new { error = "Storage is temporarily unavailable." });
    }
});
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/health", () => Results.Ok(new { status = "ready" }));
app.MapPost("/readings", (ReadingRequest request, IReadingStore store) =>
{
    var submission = store.Submit(request);
    return submission.Replay ? Results.Ok(submission) : Results.Created($"/meters/{submission.Reading.MeterId}/latest", submission);
}).RequireAuthorization("write");
app.MapGet("/meters/{meterId}/latest", (string meterId, IReadingStore store) =>
    store.Latest(meterId) is { } reading ? Results.Ok(reading) : Results.NotFound()).RequireAuthorization("read");
app.MapGet("/events", (IReadingStore store) => store.Events()).RequireAuthorization("events");
app.Run();

public partial class Program { }
