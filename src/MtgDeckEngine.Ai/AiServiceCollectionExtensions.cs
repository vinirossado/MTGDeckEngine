using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MtgDeckEngine.Core.Interfaces;

namespace MtgDeckEngine.Ai;

public static class AiServiceCollectionExtensions
{
    public static IServiceCollection AddMtgAi(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<AiOptions>(config.GetSection(AiOptions.SectionName));

        services.AddHttpClient<AnthropicAiSuggestionService>((sp, c) =>
        {
            c.Timeout = TimeSpan.FromMinutes(2);
            c.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
            c.DefaultRequestHeaders.Add("anthropic-beta", "prompt-caching-2024-07-31");
            var key = sp.GetRequiredService<IOptions<AiOptions>>().Value.AnthropicApiKey;
            if (!string.IsNullOrWhiteSpace(key))
                c.DefaultRequestHeaders.Add("x-api-key", key);
        });
        services.AddSingleton<IAiSuggestionService>(sp =>
            sp.GetRequiredService<AnthropicAiSuggestionService>());
        return services;
    }
}
