using FiapCloudGames.CatalogApi.Data;
using FiapCloudGames.CatalogApi.Domain;
using FiapCloudGames.CatalogApi.Exceptions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace FiapCloudGames.CatalogApi.Services;

public class GameService(CatalogDbContext db, IDistributedCache? cache = null)
{
    private static readonly DistributedCacheEntryOptions CacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
    };

    public async Task<List<Game>> ListAsync(CancellationToken ct = default)
    {
        const string cacheKey = "games:list";
        var cached = await GetFromCacheAsync<List<Game>>(cacheKey, ct);
        if (cached is not null) return cached;

        var games = await db.Games.AsNoTracking().OrderBy(g => g.Name).ToListAsync(ct);
        await SetCacheAsync(cacheKey, games, ct);
        return games;
    }

    public async Task<Game> FindAsync(Guid id, CancellationToken ct = default)
    {
        var cacheKey = $"games:{id}";
        var cached = await GetFromCacheAsync<Game>(cacheKey, ct);
        if (cached is not null) return cached;

        var game = await db.Games.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id, ct)
                   ?? throw new NotFoundException("Jogo nao encontrado.");
        await SetCacheAsync(cacheKey, game, ct);
        return game;
    }

    public async Task<Game> CreateAsync(string name, string description, decimal price, CancellationToken ct = default)
    {
        ValidarDados(name, price);

        var game = new Game { Name = name, Description = description, Price = price };
        db.Games.Add(game);
        await db.SaveChangesAsync(ct);
        await InvalidateListAsync(ct);
        return game;
    }

    public async Task UpdateAsync(Guid id, string name, string description, decimal price, CancellationToken ct = default)
    {
        ValidarDados(name, price);

        var game = await db.Games.FirstOrDefaultAsync(g => g.Id == id, ct)
                   ?? throw new NotFoundException("Jogo nao encontrado.");

        game.Name = name;
        game.Description = description;
        game.Price = price;
        await db.SaveChangesAsync(ct);
        await InvalidateListAsync(ct);
        await RemoveCacheAsync($"games:{id}", ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var game = await db.Games.FirstOrDefaultAsync(g => g.Id == id, ct)
                   ?? throw new NotFoundException("Jogo nao encontrado.");

        db.Games.Remove(game);
        await db.SaveChangesAsync(ct);
        await InvalidateListAsync(ct);
        await RemoveCacheAsync($"games:{id}", ct);
    }

    public async Task SeedInitialGamesAsync()
    {
        if (await db.Games.AnyAsync()) return;

        db.Games.AddRange(
            new Game { Name = "Aventura Espacial", Description = "Explore galaxias desconhecidas.", Price = 99.90m },
            new Game { Name = "Corrida Extrema", Description = "Corridas de alta velocidade.", Price = 79.90m },
            new Game { Name = "Reino Perdido", Description = "RPG de fantasia medieval.", Price = 129.90m }
        );

        await db.SaveChangesAsync();
        await InvalidateListAsync();
    }

    private static void ValidarDados(string name, decimal price)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Nome do jogo e obrigatorio.");

        if (price < 0)
            throw new ArgumentException("Preco nao pode ser negativo.");
    }

    private async Task<T?> GetFromCacheAsync<T>(string key, CancellationToken ct = default)
    {
        if (cache is null) return default;

        var json = await cache.GetStringAsync(key, ct);
        return string.IsNullOrWhiteSpace(json) ? default : JsonSerializer.Deserialize<T>(json);
    }

    private async Task SetCacheAsync<T>(string key, T value, CancellationToken ct = default)
    {
        if (cache is null) return;

        var json = JsonSerializer.Serialize(value);
        await cache.SetStringAsync(key, json, CacheOptions, ct);
    }

    private Task InvalidateListAsync(CancellationToken ct = default) =>
        RemoveCacheAsync("games:list", ct);

    private async Task RemoveCacheAsync(string key, CancellationToken ct = default)
    {
        if (cache is null) return;
        await cache.RemoveAsync(key, ct);
    }
}
