# Usa l'immagine base di .NET Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS base
# libgssapi-krb5-2 already included in aspnet:10.0-preview base image
WORKDIR /app

# Usa l'immagine SDK per la compilazione
FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src
COPY ["FlowScheduler.BackgroundJobs/FlowScheduler.BackgroundJobs.csproj", "FlowScheduler.BackgroundJobs/"]
COPY ["FlowScheduler.Core/FlowScheduler.Core.csproj", "FlowScheduler.Core/"]
COPY ["FlowScheduler.Infrastructure/FlowScheduler.Infrastructure.csproj", "FlowScheduler.Infrastructure/"]

#RUN dotnet restore "FlowScheduler.BackgroundJobs/FlowScheduler.BackgroundJobs.csproj"
COPY . .
WORKDIR "/src/FlowScheduler.BackgroundJobs"
RUN dotnet build "FlowScheduler.BackgroundJobs.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "FlowScheduler.BackgroundJobs.csproj" -c Release -o /app/publish
#RUN ls -l /app | grep Microsoft.Bcl.AsyncInterfaces

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "FlowScheduler.BackgroundJobs.dll"]