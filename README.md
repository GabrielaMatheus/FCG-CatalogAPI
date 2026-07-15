# FIAP Cloud Games - CatalogAPI

Microsservico responsavel pelo CRUD de jogos, pela biblioteca de jogos de
cada usuario e por iniciar o fluxo de compra.

## Responsabilidades

- CRUD de jogos (`/api/games`), com escrita restrita a administradores.
- Consulta da biblioteca de um usuario (`/api/users/{userId}/library`).
- Inicio da compra de um jogo (`/api/users/{userId}/games/{gameId}/purchase`):
  cria um pedido `Pending` e publica `OrderPlacedEvent`.
- Consumo do `PaymentProcessedEvent` publicado pela PaymentsAPI: atualiza o
  pedido e, se `Approved`, adiciona o jogo na biblioteca do usuario.

## Fluxo de compra

1. Cliente autenticado chama `POST /api/users/{userId}/games/{gameId}/purchase`.
2. CatalogAPI valida se o usuario ja nao possui o jogo, cria o pedido (`Pending`)
   e publica `OrderPlacedEvent { OrderId, UserId, GameId, UserEmail, GameName, Price, PlacedAtUtc }`.
3. PaymentsAPI consome o evento, simula o pagamento e publica `PaymentProcessedEvent`
   com `Status = Approved` ou `Rejected`.
4. CatalogAPI consome o `PaymentProcessedEvent`: atualiza o pedido e, se aprovado,
   adiciona o jogo na biblioteca do usuario.

## Autenticacao

A CatalogAPI **nao emite** tokens, apenas valida os tokens JWT emitidos pela
UsersAPI. Por isso `Jwt:Issuer`, `Jwt:Audience` e `Jwt:SecretKey` devem ser
identicos aos configurados na UsersAPI.

> Importante (alinhar com o parceiro): o endpoint de compra le o e-mail do
> usuario a partir do claim `ClaimTypes.Email`/`email` do token. Confirme que
> o `JwtTokenService` da UsersAPI inclui esse claim ao gerar o token -
> caso contrario, adaptar aqui para o nome de claim correto.

## Variaveis de ambiente

| Variavel | Descricao | Sensivel |
|---|---|---|
| `ConnectionStrings__CatalogDatabase` | Connection string do SQLite | Sim (Secret) |
| `Jwt__Issuer` | Emissor esperado no token | Nao (ConfigMap) |
| `Jwt__Audience` | Audiencia esperada no token | Nao (ConfigMap) |
| `Jwt__SecretKey` | Chave usada para validar a assinatura do token (>= 32 chars) | Sim (Secret) |
| `RabbitMq__Host` | Host do RabbitMQ | Nao (ConfigMap) |
| `RabbitMq__Username` | Usuario do RabbitMQ | Nao (ConfigMap) |
| `RabbitMq__Password` | Senha do RabbitMQ | Sim (Secret) |

## Executando localmente

```bash
cd src/FiapCloudGames.CatalogApi
dotnet run
```

Swagger disponivel em `http://localhost:<porta>/swagger`.

## Rodando os testes

```bash
cd tests/FiapCloudGames.CatalogApi.Tests
dotnet test
```

## Docker

```bash
docker build -t catalog-api:latest .
docker run -p 5102:8080 \
  -e ConnectionStrings__CatalogDatabase="Data Source=/data/catalog.db" \
  -e Jwt__Issuer=FiapCloudGames.UsersApi \
  -e Jwt__Audience=FiapCloudGames \
  -e Jwt__SecretKey=development-only-secret-key-change-me-123456789 \
  -e RabbitMq__Host=host.docker.internal \
  catalog-api:latest
```

## Kubernetes

Manifestos em `/k8s`: `ConfigMap`, `Secret`, `Deployment` e `Service`.

```bash
kubectl apply -f k8s/
```

O servico fica acessivel dentro do cluster em `http://catalog-api:80`.
