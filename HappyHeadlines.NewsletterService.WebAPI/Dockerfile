# Build context is the repository root (see docker-compose.yml).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore against the csproj files first for better layer caching.
COPY HappyHeadlines.Contracts/HappyHeadlines.Contracts.csproj HappyHeadlines.Contracts/
COPY HappyHeadlines.Db/HappyHeadlines.Db.csproj HappyHeadlines.Db/
COPY HappyHeadlines.Core/HappyHeadlines.Core.csproj HappyHeadlines.Core/
COPY HappyHeadlines.Observability/HappyHeadlines.Observability.csproj HappyHeadlines.Observability/
COPY HappyHeadlines.NewsletterService.WebAPI/HappyHeadlines.NewsletterService.WebAPI.csproj HappyHeadlines.NewsletterService.WebAPI/
RUN dotnet restore HappyHeadlines.NewsletterService.WebAPI/HappyHeadlines.NewsletterService.WebAPI.csproj

COPY HappyHeadlines.Contracts/ HappyHeadlines.Contracts/
COPY HappyHeadlines.Db/ HappyHeadlines.Db/
COPY HappyHeadlines.Core/ HappyHeadlines.Core/
COPY HappyHeadlines.Observability/ HappyHeadlines.Observability/
COPY HappyHeadlines.NewsletterService.WebAPI/ HappyHeadlines.NewsletterService.WebAPI/
RUN dotnet publish HappyHeadlines.NewsletterService.WebAPI/HappyHeadlines.NewsletterService.WebAPI.csproj -c Release -o /app --no-restore -p:DebugType=None -p:DebugSymbols=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
# curl is used by the container HEALTHCHECK in docker-compose.yml.
RUN apk add --no-cache curl
WORKDIR /app
COPY --from=build /app ./
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "HappyHeadlines.NewsletterService.WebAPI.dll"]
