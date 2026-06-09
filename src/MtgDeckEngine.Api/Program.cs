using MtgDeckEngine.Ai;
using MtgDeckEngine.Core.Interfaces;
using MtgDeckEngine.Graph;
using MtgDeckEngine.Graph.Repositories;
using MtgDeckEngine.Graph.Validation;
using MtgDeckEngine.Ingestion;

var builder = WebApplication.CreateBuilder(args);

// Optional gitignored local overrides for secrets — used only when
// `dotnet user-secrets` isn't a convenient fit.
builder.Configuration.AddJsonFile("appsettings.Local.json",
    optional: true, reloadOnChange: true);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

// CORS — open in Development for the Angular dev server (ng serve, :4200).
// In Production the frontend is served from the same origin, so no CORS is needed.
const string DevCorsPolicy = "dev-frontend";
builder.Services.AddCors(o => o.AddPolicy(DevCorsPolicy, p =>
    p.WithOrigins("http://localhost:4200", "http://127.0.0.1:4200")
     .AllowAnyHeader()
     .AllowAnyMethod()));

builder.Services.Configure<FusekiOptions>(builder.Configuration.GetSection(FusekiOptions.SectionName));

builder.Services.AddHttpClient<FusekiGraphRepository>(c =>
{
    c.DefaultRequestHeaders.Add("Accept", "application/sparql-results+json");
});
builder.Services.AddSingleton<IGraphRepository>(sp =>
    sp.GetRequiredService<FusekiGraphRepository>());

builder.Services.AddSingleton<ShaclValidator>();
builder.Services.AddSingleton<IDeckRecommendationService, DeckRecommendationService>();
builder.Services.AddSingleton<IFormatService, FormatService>();

builder.Services.AddMtgIngestion(builder.Configuration);
builder.Services.AddMtgAi(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

if (app.Environment.IsDevelopment())
    app.UseCors(DevCorsPolicy);

app.UseHttpsRedirection();
app.MapControllers();

// Liveness: in-process only, never touches the DB. Keep this fast so a
// transient SPARQL outage doesn't cascade into pod restarts.
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// Readiness: actually probes the triplestore with a cheap SPARQL ASK.
// k8s removes the pod from the LB when this fails, but does not restart it.
app.MapGet("/health/ready", async (IGraphRepository repo, CancellationToken ct) =>
{
    try
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(3));
        await repo.QueryAsync("ASK WHERE { ?s ?p ?o }", cts.Token);
        return Results.Ok(new { status = "ready" });
    }
    catch (Exception ex)
    {
        return Results.Json(new { status = "not_ready", error = ex.Message },
            statusCode: 503);
    }
});

app.Run();

public partial class Program;
