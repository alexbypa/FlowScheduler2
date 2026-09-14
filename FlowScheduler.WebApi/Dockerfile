# Vedere https://aka.ms/customizecontainer per informazioni su come personalizzare il contenitore di debug e su come Visual Studio usa questo Dockerfile per compilare le immagini per un debug più rapido.
# Questa fase viene usata durante l'esecuzione da Visual Studio in modalità rapida (impostazione predefinita per la configurazione di debug)
FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080


# Questa fase viene usata per compilare il progetto di servizio
FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["FlowScheduler.WebApi/FlowScheduler.WebApi.csproj", "FlowScheduler.WebApi/"]
COPY ["FlowScheduler.Infrastructure/FlowScheduler.Infrastructure.csproj", "FlowScheduler.Infrastructure/"]
COPY ["FlowScheduler.Core/FlowScheduler.Core.csproj", "FlowScheduler.Core/"]
RUN dotnet restore "./FlowScheduler.WebApi/FlowScheduler.WebApi.csproj"
COPY . .
WORKDIR "/src/FlowScheduler.WebApi"
RUN dotnet build "./FlowScheduler.WebApi.csproj" -c $BUILD_CONFIGURATION -o /app/build

# Questa fase viene usata per pubblicare il progetto di servizio da copiare nella fase finale
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./FlowScheduler.WebApi.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# Questa fase viene usata nell'ambiente di produzione o durante l'esecuzione da Visual Studio in modalità normale (impostazione predefinita quando non si usa la configurazione di debug)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "FlowScheduler.WebApi.dll"]