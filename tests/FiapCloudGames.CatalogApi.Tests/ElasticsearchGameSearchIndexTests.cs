using System.Net;
using System.Text;
using FiapCloudGames.CatalogApi.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FiapCloudGames.CatalogApi.Tests;

public sealed class ElasticsearchGameSearchIndexTests
{
    [Fact]
    public async Task SearchAsync_deve_enviar_fuzzy_search_e_ler_score_de_relevancia()
    {
        string? requestBody = null;
        var handler = new StubHandler(async request =>
        {
            requestBody = await request.Content!.ReadAsStringAsync();
            var json = """
                       {
                         "hits": {
                           "hits": [
                             {
                               "_score": 7.5,
                               "_source": {
                                 "id": "d2719ac2-3d71-46b5-8c9b-61d99adb3453",
                                 "name": "Aventura Espacial",
                                 "description": "Exploracao da galaxia",
                                 "price": 99.90
                               }
                             }
                           ]
                         }
                       }
                       """;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://elastic.example/") };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Elasticsearch:IndexName"] = "fcg-games"
            })
            .Build();
        var index = new ElasticsearchGameSearchIndex(
            httpClient,
            configuration,
            NullLogger<ElasticsearchGameSearchIndex>.Instance);

        var result = await index.SearchAsync("Avetura", 20);

        var hit = Assert.Single(result);
        Assert.Equal("Aventura Espacial", hit.Game.Name);
        Assert.Equal(7.5, hit.Score);
        Assert.NotNull(requestBody);
        Assert.Contains("\"fuzziness\":\"AUTO\"", requestBody);
        Assert.Contains("\"name^3\"", requestBody);
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => responseFactory(request);
    }
}
