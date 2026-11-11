using FiapCloudGames.CatalogApi.Data;
using FiapCloudGames.CatalogApi.Domain;
using FiapCloudGames.CatalogApi.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FiapCloudGames.CatalogApi.Tests;

public sealed class GameServiceSearchSyncTests
{
    [Fact]
    public async Task CreateAndUpdateAsync_devem_sincronizar_indice_de_busca()
    {
        await using var db = CreateContext();
        var index = new RecordingSearchIndex();
        var service = new GameService(db, searchIndex: index);

        var game = await service.CreateAsync("Aventura", "Exploracao espacial", 99.90m);
        await service.UpdateAsync(game.Id, "Aventura Galactica", "Exploracao espacial", 109.90m);

        Assert.Equal(2, index.Indexed.Count);
        Assert.Equal("Aventura Galactica", index.Indexed[^1].Name);
        Assert.Equal(game.Id, index.Indexed[^1].Id);
    }

    [Fact]
    public async Task DeleteAsync_deve_remover_documento_do_indice_de_busca()
    {
        await using var db = CreateContext();
        var index = new RecordingSearchIndex();
        var service = new GameService(db, searchIndex: index);
        var game = await service.CreateAsync("Corrida", "Alta velocidade", 59.90m);

        await service.DeleteAsync(game.Id);

        Assert.Contains(game.Id, index.Removed);
    }

    private static CatalogDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        var db = new CatalogDbContext(options);
        db.Database.OpenConnection();
        db.Database.EnsureCreated();
        return db;
    }

    private sealed class RecordingSearchIndex : IGameSearchIndex
    {
        public List<Game> Indexed { get; } = [];
        public List<Guid> Removed { get; } = [];

        public Task EnsureCreatedAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task IndexAsync(Game game, CancellationToken ct = default)
        {
            Indexed.Add(new Game
            {
                Id = game.Id,
                Name = game.Name,
                Description = game.Description,
                Price = game.Price
            });
            return Task.CompletedTask;
        }

        public Task RemoveAsync(Guid gameId, CancellationToken ct = default)
        {
            Removed.Add(gameId);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<GameSearchHit>> SearchAsync(
            string query,
            int limit,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<GameSearchHit>>([]);
    }
}
