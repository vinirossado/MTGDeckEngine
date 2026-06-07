using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MtgDeckEngine.Ingestion.Http;
using MtgDeckEngine.Ingestion.Workers;

namespace MtgDeckEngine.Ingestion;

public static class IngestionServiceCollectionExtensions
{
    public static IServiceCollection AddMtgIngestion(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<IngestionOptions>(config.GetSection(IngestionOptions.SectionName));

        services.AddHttpClient<ScryfallClient>(c =>
        {
            c.BaseAddress = new Uri("https://api.scryfall.com/");
            c.DefaultRequestHeaders.Add("Accept", "application/json");
            c.DefaultRequestHeaders.Add("User-Agent", "MtgDeckEngine/0.1 (+phase1)");
        });
        services.AddHttpClient(nameof(ScryfallBulkCache), c =>
        {
            c.BaseAddress = new Uri("https://api.scryfall.com/");
            c.DefaultRequestHeaders.Add("Accept", "application/json");
            c.DefaultRequestHeaders.Add("User-Agent", "MtgDeckEngine/0.1 (+phase1)");
            c.Timeout = TimeSpan.FromMinutes(5);
        });
        services.AddSingleton<ScryfallBulkCache>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var http = factory.CreateClient(nameof(ScryfallBulkCache));
            var logger = sp.GetRequiredService<ILogger<ScryfallBulkCache>>();
            return new ScryfallBulkCache(http, logger);
        });
        services.AddHttpClient<EdhrecClient>(c =>
        {
            c.BaseAddress = new Uri("https://json.edhrec.com/");
            c.DefaultRequestHeaders.Add("Accept", "application/json");
            c.DefaultRequestHeaders.Add("User-Agent", "MtgDeckEngine/0.1 (+phase1)");
        });

        services.AddScoped<CommanderIngestor>();
        services.AddHostedService<StartupIngestionWorker>();
        return services;
    }
}
