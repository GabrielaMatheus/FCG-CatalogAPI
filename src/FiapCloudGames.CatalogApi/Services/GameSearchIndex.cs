using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using FiapCloudGames.CatalogApi.Data;
using FiapCloudGames.CatalogApi.Domain;
using Microsoft.EntityFrameworkCore;

namespace FiapCloudGames.CatalogApi.Services;

public sealed record GameSearchHit(Game Game, double Score);

public interface IGameSearchIndex
{
    Task EnsureCreatedAsync(CancellationToken ct = default);
    Task IndexAsync(Game game, CancellationToken ct = default);
    Task RemoveAsync(Guid gameId, CancellationToken ct = default);
    Task<IReadOnlyList<GameSearchHit>> SearchAsync(string query, int limit, CancellationToken ct = default);
}

public sealed class ElasticsearchGameSearchIndex(
    HttpClient client,
    IConfiguration configuration,
    ILogger<ElasticsearchGameSearchIndex> logger) : IGameSearchIndex
{
    private readonly string _indexName = configuration["Elasticsearch:IndexName"] ?? "fcg-games";

    public async Task EnsureCreatedAsync(CancellationToken ct = default)
    {
        using var existsRequest = new HttpRequestMessage(HttpMethod.Head, _indexName);
        using var exists = await client.SendAsync(existsRequest, ct);
        if (exists.IsSuccessStatusCode) return;
        if (exists.StatusCode != HttpStatusCode.NotFound)
            await EnsureSuccessAsync(exists, "consultar o indice", ct);

        var definition = new
        {
            mappings = new
            {
                properties = new
                {
                    id = new { type = "keyword" },
                    name = new { type = "text" },
                    description = new { type = "text" },
                    price = new { type = "double" }
                }
            }
        };

        using var created = await client.PutAsJsonAsync(_indexName, definition, ct);
        if (!created.IsSuccessStatusCode)
        {
            var detail = await created.Content.ReadAsStringAsync(ct);
            var concurrentCreation = created.StatusCode == HttpStatusCode.BadRequest
                                     && detail.Contains("resource_already_exists_exception", StringComparison.Ordinal);
            if (!concurrentCreation)
                throw new InvalidOperationException(
                    $"Nao foi possivel criar o indice no Elasticsearch. HTTP {(int)created.StatusCode}: {detail}");
        }

        logger.LogInformation("Indice Elasticsearch {IndexName} disponivel.", _indexName);
    }

    public async Task IndexAsync(Game game, CancellationToken ct = default)
    {
        var document = GameSearchDocument.From(game);
        using var response = await client.PutAsJsonAsync(
            $"{_indexName}/_doc/{game.Id:D}?refresh=wait_for",
            document,
            ct);
        await EnsureSuccessAsync(response, $"indexar o jogo {game.Id}", ct);
    }

    public async Task RemoveAsync(Guid gameId, CancellationToken ct = default)
    {
        using var response = await client.DeleteAsync(
            $"{_indexName}/_doc/{gameId:D}?refresh=wait_for",
            ct);

        if (response.StatusCode != HttpStatusCode.NotFound)
            await EnsureSuccessAsync(response, $"remover o jogo {gameId} do indice", ct);
    }

    public async Task<IReadOnlyList<GameSearchHit>> SearchAsync(
        string query,
        int limit,
        CancellationToken ct = default)
    {
        var request = new
        {
            size = limit,
            query = new
            {
                multi_match = new
                {
                    query,
                    fields = new[] { "name^3", "description" },
                    type = "best_fields",
                    fuzziness = "AUTO",
                    fuzzy_transpositions = true
                }
            }
        };

        using var response = await client.PostAsJsonAsync($"{_indexName}/_search", request, ct);
        await EnsureSuccessAsync(response, "realizar a busca", ct);

        var result = await response.Content.ReadFromJsonAsync<ElasticsearchSearchResponse>(cancellationToken: ct)
                     ?? throw new InvalidOperationException("O Elasticsearch retornou uma resposta vazia.");

        return result.Hits.Hits
            .Where(hit => hit.Source is not null)
            .Select(hit => new GameSearchHit(hit.Source!.ToGame(), hit.Score))
            .ToList();
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        var detail = await response.Content.ReadAsStringAsync(ct);
        throw new InvalidOperationException(
            $"Nao foi possivel {operation} no Elasticsearch. HTTP {(int)response.StatusCode}: {detail}");
    }
}

public sealed class GameSearchBootstrapper(
    IServiceScopeFactory scopeFactory,
    IGameSearchIndex searchIndex,
    ILogger<GameSearchBootstrapper> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await searchIndex.EnsureCreatedAsync(stoppingToken);

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var games = await db.Games.AsNoTracking().ToListAsync(stoppingToken);
        foreach (var game in games)
            await searchIndex.IndexAsync(game, stoppingToken);

        logger.LogInformation("Indice Elasticsearch sincronizado com {Count} jogos.", games.Count);
    }
}

internal sealed class ElasticsearchSearchResponse
{
    public ElasticsearchHits Hits { get; init; } = new();
}

internal sealed class ElasticsearchHits
{
    public List<ElasticsearchHit> Hits { get; init; } = [];
}

internal sealed class ElasticsearchHit
{
    [JsonPropertyName("_score")]
    public double Score { get; init; }

    [JsonPropertyName("_source")]
    public GameSearchDocument? Source { get; init; }
}

internal sealed class GameSearchDocument
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public decimal Price { get; init; }

    public static GameSearchDocument From(Game game) => new()
    {
        Id = game.Id,
        Name = game.Name,
        Description = game.Description,
        Price = game.Price
    };

    public Game ToGame() => new()
    {
        Id = Id,
        Name = Name,
        Description = Description,
        Price = Price
    };
}
