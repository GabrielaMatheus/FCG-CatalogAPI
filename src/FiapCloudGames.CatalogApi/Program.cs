using System.Security.Claims;
using System.Text;
using FiapCloudGames.CatalogApi.Consumers;
using FiapCloudGames.CatalogApi.Data;
using FiapCloudGames.CatalogApi.Exceptions;
using FiapCloudGames.CatalogApi.Services;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

var jwtSecret = builder.Configuration["Jwt:SecretKey"];
if (string.IsNullOrWhiteSpace(jwtSecret) || jwtSecret.Length < 32)
    throw new InvalidOperationException("Jwt:SecretKey deve ter pelo menos 32 caracteres.");
// IMPORTANTE: precisa ser o MESMO segredo/issuer/audience configurados na UsersAPI,
// pois a CatalogAPI apenas valida tokens emitidos por ela - nao emite tokens propios.

builder.Services.AddDbContext<CatalogDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("CatalogDatabase")));

builder.Services.AddScoped<GameService>();
builder.Services.AddScoped<OrderService>();

builder.Services.AddMassTransit(bus =>
{
    bus.AddConsumer<PaymentProcessedConsumer>();
    bus.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration["RabbitMq:Host"] ?? "localhost", "/", host =>
        {
            host.Username(builder.Configuration["RabbitMq:Username"] ?? "guest");
            host.Password(builder.Configuration["RabbitMq:Password"] ?? "guest");
        });

        cfg.ReceiveEndpoint("catalog-payment-processed", endpoint =>
        {
            endpoint.UseMessageRetry(retry => retry.Intervals(1000, 5000, 15000));
            endpoint.ConfigureConsumer<PaymentProcessedConsumer>(context);
        });
    });
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
        RoleClaimType = ClaimTypes.Role
    };
});

builder.Services.AddAuthorization(options =>
    options.AddPolicy("Administrator", policy => policy.RequireRole("Administrador", "Administrator")));
// Aceita os dois nomes de role para nao depender de como a UsersAPI grava o claim
// (no monolito original era "Administrador"; ajustar aqui se o parceiro usar outro valor).

builder.Services.AddHealthChecks();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "FCG Catalog API", Version = "v1" });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme
        {
            Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
        }] = Array.Empty<string>()
    });
});

var app = builder.Build();
app.UseMiddleware<ApiExceptionMiddleware>();
app.UseSwagger();
app.UseSwaggerUI();
app.UseAuthentication();
app.UseAuthorization();
app.MapHealthChecks("/health");

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
    await db.Database.EnsureCreatedAsync();
    await scope.ServiceProvider.GetRequiredService<GameService>().SeedInitialGamesAsync();
}

var games = app.MapGroup("/api/games").WithTags("Games");

games.MapGet("/", async (GameService service, CancellationToken ct) =>
    Results.Ok((await service.ListAsync(ct)).Select(g => g.ToResponse())))
    .RequireAuthorization();

games.MapGet("/{id:guid}", async (Guid id, GameService service, CancellationToken ct) =>
    Results.Ok((await service.FindAsync(id, ct)).ToResponse()))
    .RequireAuthorization();

games.MapPost("/", async (CreateGameRequest request, GameService service, CancellationToken ct) =>
{
    var game = await service.CreateAsync(request.Name, request.Description, request.Price, ct);
    return Results.Created($"/api/games/{game.Id}", game.ToResponse());
})
.RequireAuthorization("Administrator");

games.MapPut("/{id:guid}", async (Guid id, UpdateGameRequest request, GameService service, CancellationToken ct) =>
{
    await service.UpdateAsync(id, request.Name, request.Description, request.Price, ct);
    return Results.NoContent();
})
.RequireAuthorization("Administrator");

games.MapDelete("/{id:guid}", async (Guid id, GameService service, CancellationToken ct) =>
{
    await service.DeleteAsync(id, ct);
    return Results.NoContent();
})
.RequireAuthorization("Administrator");

var users = app.MapGroup("/api/users").WithTags("Biblioteca e Compras");

users.MapGet("/{userId:guid}/library", async (Guid userId, ClaimsPrincipal principal, OrderService service, CancellationToken ct) =>
{
    var erro = principal.ValidarDonoOuAdmin(userId);
    if (erro != null) return erro;

    var jogos = await service.ListLibraryAsync(userId, ct);
    return Results.Ok(jogos.Select(g => g.ToResponse()));
})
.RequireAuthorization();

users.MapPost("/{userId:guid}/games/{gameId:guid}/purchase", async (
    Guid userId,
    Guid gameId,
    ClaimsPrincipal principal,
    OrderService service,
    CancellationToken ct) =>
{
    var erro = principal.ValidarDonoOuAdmin(userId);
    if (erro != null) return erro;

    var email = principal.FindFirstValue(ClaimTypes.Email)
                ?? principal.FindFirstValue("email")
                ?? principal.FindFirstValue(ClaimTypes.Name);

    if (string.IsNullOrWhiteSpace(email))
        return Results.BadRequest(new { erro = "Token nao contem um claim de e-mail valido." });

    var order = await service.PlaceOrderAsync(userId, gameId, email, ct);

    return Results.Accepted($"/api/orders/{order.Id}", new
    {
        mensagem = "Compra iniciada, o pagamento sera processado de forma assincrona.",
        orderId = order.Id,
        status = order.Status.ToString()
    });
})
.RequireAuthorization();

app.Run();

public partial class Program;

public sealed record CreateGameRequest(string Name, string Description, decimal Price);
public sealed record UpdateGameRequest(string Name, string Description, decimal Price);
public sealed record GameResponse(Guid Id, string Name, string Description, decimal Price);

internal static class ResponseMapping
{
    public static GameResponse ToResponse(this FiapCloudGames.CatalogApi.Domain.Game game) =>
        new(game.Id, game.Name, game.Description, game.Price);
}

internal static class AuthorizationExtensions
{
    // Permite a acao somente se o usuario logado for o dono do recurso ou um administrador.
    public static IResult? ValidarDonoOuAdmin(this ClaimsPrincipal principal, Guid userId)
    {
        var perfil = principal.FindFirstValue(ClaimTypes.Role);
        var usuarioLogado = principal.FindFirstValue(ClaimTypes.NameIdentifier);

        var isAdmin = perfil is "Administrador" or "Administrator";
        if (isAdmin || usuarioLogado == userId.ToString())
            return null;

        return Results.Json(new { erro = "Voce so pode acessar seus proprios dados." }, statusCode: 403);
    }
}

internal sealed class ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try { await next(context); }
        catch (NotFoundException ex) { await WriteAsync(context, 404, ex.Message); }
        catch (ConflictException ex) { await WriteAsync(context, 409, ex.Message); }
        catch (UnauthorizedAccessException ex) { await WriteAsync(context, 401, ex.Message); }
        catch (ArgumentException ex) { await WriteAsync(context, 400, ex.Message); }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled request error");
            await WriteAsync(context, 500, "Erro interno.");
        }
    }

    private static async Task WriteAsync(HttpContext context, int status, string message)
    {
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new { erro = message });
    }
}
