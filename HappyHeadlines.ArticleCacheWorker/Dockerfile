# Build context is the repository root (see docker-compose.yml).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore against the csproj files first for better layer caching.
COPY HappyHeadlines.Contracts/HappyHeadlines.Contracts.csproj HappyHeadlines.Contracts/
COPY HappyHeadlines.Db/HappyHeadlines.Db.csproj HappyHeadlines.Db/
COPY HappyHeadlines.Core/HappyHeadlines.Core.csproj HappyHeadlines.Core/
COPY HappyHeadlines.Observability/HappyHeadlines.Observability.csproj HappyHeadlines.Observability/
COPY HappyHeadlines.ArticleCacheWorker/HappyHeadlines.ArticleCacheWorker.csproj HappyHeadlines.ArticleCacheWorker/
RUN dotnet restore HappyHeadlines.ArticleCacheWorker/HappyHeadlines.ArticleCacheWorker.csproj

COPY HappyHeadlines.Contracts/ HappyHeadlines.Contracts/
COPY HappyHeadlines.Db/ HappyHeadlines.Db/
COPY HappyHeadlines.Core/ HappyHeadlines.Core/
COPY HappyHeadlines.Observability/ HappyHeadlines.Observability/
COPY HappyHeadlines.ArticleCacheWorker/ HappyHeadlines.ArticleCacheWorker/
RUN dotnet publish HappyHeadlines.ArticleCacheWorker/HappyHeadlines.ArticleCacheWorker.csproj -c Release -o /app --no-restore -p:DebugType=None -p:DebugSymbols=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
# SqlClient does not work in invariant globalization mode, which the alpine image turns on.
RUN apk add --no-cache icu-libs
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
WORKDIR /app
COPY --from=build /app ./
ENTRYPOINT ["dotnet", "HappyHeadlines.ArticleCacheWorker.dll"]
