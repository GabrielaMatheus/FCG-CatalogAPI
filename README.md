# FIAP Cloud Games - CatalogAPI - Fase 4

Microsservico .NET 8 responsavel pelo catalogo, compras, biblioteca, avaliacoes em MongoDB, cache distribuido e busca avancada.

## Funcionalidades

- CRUD relacional de jogos, com escrita restrita a administradores.
- Avaliacoes flexiveis em MongoDB usando o driver oficial `MongoDB.Driver`.
- Cache de consultas com `IDistributedCache` e Redis; as respostas de leitura informam `X-Cache: HIT|MISS`.
- Indexacao no Elasticsearch quando um jogo e criado, editado ou removido.
- Reconciliacao do indice com o banco relacional na inicializacao.
- `GET /search?q=...&limit=...` com `multi_match`, fuzziness `AUTO`, peso maior para o nome e ordenacao por `_score`.
- Compras assincronas por RabbitMQ/MassTransit.
- Metricas Prometheus, health check, JWT e Swagger.

## Persistencia poliglota

| Dado | Tecnologia | Motivo |
| --- | --- | --- |
| Jogos, pedidos e biblioteca | PostgreSQL em producao; SQLite no desenvolvimento | Consistencia transacional |
| Avaliacoes e metadata flexivel | MongoDB | Documento flexivel e crescimento independente |
| Consultas de catalogo | Redis | Reducao de latencia e round-trips |
| Indice de busca | Elastic Cloud hospedado no Microsoft Azure | Fuzzy search e ranking por relevancia |

## Endpoints da Fase 4

| Metodo | Rota | Descricao |
| --- | --- | --- |
| GET | `/search?q=Avetura&limit=20` | Busca tolerante a erro de digitacao, ordenada por relevancia |
| GET | `/api/games` | Lista jogos e informa `X-Cache` |
| GET | `/api/games/{id}` | Consulta um jogo e informa `X-Cache` |
| POST | `/api/games` | Cria e indexa um jogo |
| PUT | `/api/games/{id}` | Atualiza o jogo e o indice |
| DELETE | `/api/games/{id}` | Remove o jogo e o documento do indice |
| GET/POST | `/api/games/{id}/reviews` | Consulta ou cria avaliacao no MongoDB |

Todas as rotas exigem JWT; operacoes de escrita no catalogo exigem o perfil de administrador.

## Configuracao

Valores sensiveis nao ficam em `appsettings.json` nem nos manifestos. Em producao, o repositorio de orquestracao sincroniza o Azure Key Vault com o Secret `fcg-runtime` do Kubernetes.

| Variavel | Origem em producao |
| --- | --- |
| `ConnectionStrings__CatalogDatabase` | Azure Key Vault - PostgreSQL Flexible Server |
| `ConnectionStrings__MongoDb` | Azure Key Vault - Cosmos DB for MongoDB |
| `ConnectionStrings__Redis` | Azure Key Vault - Azure Managed Redis |
| `Elasticsearch__Uri`, `Elasticsearch__Username`, `Elasticsearch__Password` | Azure Key Vault - Elastic Cloud on Azure |
| `Jwt__SecretKey` | Azure Key Vault |
| `RabbitMq__Username`, `RabbitMq__Password` | Azure Key Vault |

Configuracoes nao sensiveis: `Database__Provider`, `MongoDb__Database`, `Elasticsearch__IndexName`, issuer/audience JWT e host do RabbitMQ.

## Testes

```powershell
dotnet test tests/FiapCloudGames.CatalogApi.Tests/FiapCloudGames.CatalogApi.Tests.csproj
```

Os testes cobrem o fluxo de compra e a sincronizacao do indice em criacao, edicao e exclusao de jogos.

## CI/CD

`.github/workflows/ci-cd.yml` executa build, testes, build da imagem, Trivy, push com tag do commit para o Azure Container Registry e rolling update no AKS. A autenticacao Azure usa OIDC, sem client secret persistente.

Configure o GitHub Environment `production` com estas variables:

- `AZURE_CLIENT_ID`
- `AZURE_TENANT_ID`
- `AZURE_SUBSCRIPTION_ID`
- `AZURE_RESOURCE_GROUP`
- `AKS_CLUSTER_NAME`
- `ACR_NAME`
- `ACR_LOGIN_SERVER`
- `ACR_REPOSITORY=fcg-catalog-api`

O provisionamento e o deploy inicial estao documentados no repositorio `FCG-Orchestration`.
