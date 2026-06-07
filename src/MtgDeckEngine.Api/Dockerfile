FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY MtgDeckEngine.sln ./
COPY src/MtgDeckEngine.Core/MtgDeckEngine.Core.csproj          src/MtgDeckEngine.Core/
COPY src/MtgDeckEngine.Graph/MtgDeckEngine.Graph.csproj        src/MtgDeckEngine.Graph/
COPY src/MtgDeckEngine.Ingestion/MtgDeckEngine.Ingestion.csproj src/MtgDeckEngine.Ingestion/
COPY src/MtgDeckEngine.Ontology/MtgDeckEngine.Ontology.csproj  src/MtgDeckEngine.Ontology/
COPY src/MtgDeckEngine.Api/MtgDeckEngine.Api.csproj            src/MtgDeckEngine.Api/
COPY tests/MtgDeckEngine.UnitTests/MtgDeckEngine.UnitTests.csproj           tests/MtgDeckEngine.UnitTests/
COPY tests/MtgDeckEngine.IntegrationTests/MtgDeckEngine.IntegrationTests.csproj tests/MtgDeckEngine.IntegrationTests/

RUN dotnet restore src/MtgDeckEngine.Api/MtgDeckEngine.Api.csproj

COPY . .
RUN dotnet publish src/MtgDeckEngine.Api/MtgDeckEngine.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "MtgDeckEngine.Api.dll"]
