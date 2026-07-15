using FiapCloudGames.CatalogApi.Data;
using FiapCloudGames.CatalogApi.Domain;
using FiapCloudGames.CatalogApi.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace FiapCloudGames.CatalogApi.Services;

public class GameService(CatalogDbContext db)
{
    public async Task<List<Game>> ListAsync(CancellationToken ct = default)
        => await db.Games.AsNoTracking().OrderBy(g => g.Name).ToListAsync(ct);

    public async Task<Game> FindAsync(Guid id, CancellationToken ct = default)
        => await db.Games.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id, ct)
           ?? throw new NotFoundException("Jogo nao encontrado.");

    public async Task<Game> CreateAsync(string name, string description, decimal price, CancellationToken ct = default)
    {
        ValidarDados(name, price);

        var game = new Game { Name = name, Description = description, Price = price };
        db.Games.Add(game);
        await db.SaveChangesAsync(ct);
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
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var game = await db.Games.FirstOrDefaultAsync(g => g.Id == id, ct)
                   ?? throw new NotFoundException("Jogo nao encontrado.");

        db.Games.Remove(game);
        await db.SaveChangesAsync(ct);
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
    }

    private static void ValidarDados(string name, decimal price)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Nome do jogo e obrigatorio.");

        if (price < 0)
            throw new ArgumentException("Preco nao pode ser negativo.");
    }
}
