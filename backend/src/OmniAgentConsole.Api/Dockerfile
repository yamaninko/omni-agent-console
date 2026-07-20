FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY OmniAgentConsole.slnx ./
COPY backend/src/OmniAgentConsole.Api/OmniAgentConsole.Api.csproj backend/src/OmniAgentConsole.Api/
COPY backend/src/OmniAgentConsole.Application/OmniAgentConsole.Application.csproj backend/src/OmniAgentConsole.Application/
COPY backend/src/OmniAgentConsole.Domain/OmniAgentConsole.Domain.csproj backend/src/OmniAgentConsole.Domain/
COPY backend/src/OmniAgentConsole.Infrastructure/OmniAgentConsole.Infrastructure.csproj backend/src/OmniAgentConsole.Infrastructure/
RUN dotnet restore backend/src/OmniAgentConsole.Api/OmniAgentConsole.Api.csproj

COPY . .
RUN dotnet publish backend/src/OmniAgentConsole.Api/OmniAgentConsole.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
EXPOSE 8080
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "OmniAgentConsole.Api.dll"]
