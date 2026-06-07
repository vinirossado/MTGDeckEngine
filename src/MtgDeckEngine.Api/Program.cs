using MtgDeckEngine.Core.Interfaces;
using MtgDeckEngine.Graph;
using MtgDeckEngine.Graph.Repositories;
using MtgDeckEngine.Graph.Validation;
using MtgDeckEngine.Ingestion;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

builder.Services.Configure<FusekiOptions>(builder.Configuration.GetSection(FusekiOptions.SectionName));

builder.Services.AddHttpClient<FusekiGraphRepository>(c =>
{
    c.DefaultRequestHeaders.Add("Accept", "application/sparql-results+json");
});
builder.Services.AddSingleton<IGraphRepository>(sp =>
    sp.GetRequiredService<FusekiGraphRepository>());

builder.Services.AddSingleton<ShaclValidator>();
builder.Services.AddSingleton<IDeckRecommendationService, DeckRecommendationService>();

builder.Services.AddMtgIngestion(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

public partial class Program;
