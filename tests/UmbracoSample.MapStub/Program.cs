using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

// Test-only: every value here is synthetic and lives only as long as the process.
var builder = WebApplication.CreateSlimBuilder(args);
var app = builder.Build();

var apiKey = app.Configuration["MapStub:ApiKey"] ?? throw new InvalidOperationException("MapStub:ApiKey is required.");
var leads = new ConcurrentDictionary<string, StoredLead>(StringComparer.Ordinal);
var telemetry = new ConcurrentQueue<byte[]>();
var outage = false;

bool Authorised(HttpRequest request) => request.Headers["X-Api-Key"] == apiKey;

app.MapGet("/stub/health", () => Results.Ok());

app.MapPost("/leads", async (HttpRequest request) =>
{
    if (!Authorised(request))
    {
        return Results.StatusCode(StatusCodes.Status401Unauthorized);
    }

    if (Volatile.Read(ref outage))
    {
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }

    if (request.Headers["Idempotency-Key"].ToString() is not { Length: > 0 } key)
    {
        return Results.BadRequest();
    }

    var body = await JsonSerializer.DeserializeAsync<JsonElement>(request.Body);
    var lead = new StoredLead(
        key,
        body.GetProperty("listId").GetString(),
        body.GetProperty("consentVersion").GetString(),
        body.GetProperty("marketingConsent").GetBoolean());
    return leads.TryAdd(key, lead) ? Results.StatusCode(StatusCodes.Status201Created) : Results.Ok();
});

app.MapPost("/leads/lookup", async (HttpRequest request) =>
{
    if (!Authorised(request))
    {
        return Results.StatusCode(StatusCodes.Status401Unauthorized);
    }

    if (Volatile.Read(ref outage))
    {
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }

    var body = await JsonSerializer.DeserializeAsync<JsonElement>(request.Body);
    return leads.ContainsKey(body.GetProperty("idempotencyKey").GetString() ?? string.Empty) ? Results.Ok() : Results.NotFound();
});

// Test controls.
app.MapGet("/stub/leads", () => Results.Ok(leads.Values));
app.MapDelete("/stub/leads", () =>
{
    leads.Clear();
    return Results.NoContent();
});
app.MapPut("/stub/outage/{on:bool}", (bool on) =>
{
    Volatile.Write(ref outage, on);
    return Results.NoContent();
});

// OTLP/HTTP sink: keeps the raw protobuf bodies, which hold attribute strings verbatim.
app.MapPost("/v1/{signal}", async (string signal, HttpRequest request) =>
{
    using var buffer = new MemoryStream();
    await request.Body.CopyToAsync(buffer);
    telemetry.Enqueue(buffer.ToArray());
    return Results.Ok();
});
app.MapPost("/stub/telemetry/search", async (HttpRequest request) =>
{
    using var reader = new StreamReader(request.Body);
    var needle = Encoding.UTF8.GetBytes(await reader.ReadToEndAsync());
    return Results.Ok(new TelemetrySearch(telemetry.Count, telemetry.Any(body => body.AsSpan().IndexOf(needle) >= 0)));
});

app.Run();

internal sealed record StoredLead(string IdempotencyKey, string? ListId, string? ConsentVersion, bool MarketingConsent);

internal sealed record TelemetrySearch(int Exports, bool Found);
