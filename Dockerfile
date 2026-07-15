FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/FiapCloudGames.CatalogApi/FiapCloudGames.CatalogApi.csproj src/FiapCloudGames.CatalogApi/
RUN dotnet restore src/FiapCloudGames.CatalogApi/FiapCloudGames.CatalogApi.csproj
COPY src/FiapCloudGames.CatalogApi/ src/FiapCloudGames.CatalogApi/
RUN dotnet publish src/FiapCloudGames.CatalogApi/FiapCloudGames.CatalogApi.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
RUN adduser --disabled-password --gecos "" --home /app appuser && mkdir -p /data && chown -R appuser:appuser /app /data
USER appuser
COPY --from=build --chown=appuser:appuser /app/publish .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "FiapCloudGames.CatalogApi.dll"]
