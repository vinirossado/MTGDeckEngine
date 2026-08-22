using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MtgDeckEngine.Core.Interfaces;
using MtgDeckEngine.Ingestion.Http;
using MtgDeckEngine.Ingestion.Workers;

namespace MtgDeckEngine.Ingestion;

public static class IngestionServiceCollectionExtensions
{
    public static IServiceCollection AddMtgIngestion(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<IngestionOptions>(config.GetSection(IngestionOptions.SectionName));
        services.Configure<TopDeckOptions>(config.GetSection(TopDeckOptions.SectionName));

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
        services.AddHttpClient<EdhTop16Client>(c =>
        {
            c.BaseAddress = new Uri("https://edhtop16.com/api/graphql");
            c.DefaultRequestHeaders.Add("Accept", "application/json");
            c.DefaultRequestHeaders.Add("User-Agent", "MtgDeckEngine/0.2 (+phase2)");
            c.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddHttpClient<TopDeckClient>((sp, c) =>
        {
            c.DefaultRequestHeaders.Add("Accept", "application/json");
            c.DefaultRequestHeaders.Add("User-Agent", "MtgDeckEngine/0.3 (+phase3)");
            c.Timeout = TimeSpan.FromSeconds(60);
            // TopDeck.gg auth: bare API key in the Authorization header (no "Bearer" prefix).
            var key = sp.GetRequiredService<IOptions<TopDeckOptions>>().Value.ApiKey;
            if (!string.IsNullOrWhiteSpace(key))
                c.DefaultRequestHeaders.Add("Authorization", key);
        });

        services.AddHttpClient(CommanderSpellbookClient.HttpClientName, c =>
        {
            c.BaseAddress = new Uri("https://backend.commanderspellbook.com/");
            c.DefaultRequestHeaders.Add("Accept", "application/json");
            c.DefaultRequestHeaders.Add("User-Agent", "MtgDeckEngine/0.4 (+bracket)");
            c.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddSingleton<ICommanderNameResolver, ScryfallCommanderNameResolver>();
        services.AddSingleton<CommanderSpellbookClient>();
        // Singleton: IDeckRecommendationService is a singleton and would
        // otherwise capture a scoped bracket service.
        services.AddSingleton<IBracketService, SpellbookBracketService>();

        services.AddScoped<CommanderIngestor>();
        services.AddScoped<EdhTop16Ingestor>();
        services.AddScoped<TopDeckIngestor>();
        services.AddScoped<IngestionOrchestrator>();
        services.AddHostedService<StartupIngestionWorker>();
        return services;
    }
}
