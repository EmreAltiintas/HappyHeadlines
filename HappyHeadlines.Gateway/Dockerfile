# Build context is the repository root (see docker-compose.yml).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY HappyHeadlines.Observability/HappyHeadlines.Observability.csproj HappyHeadlines.Observability/
COPY HappyHeadlines.Gateway/HappyHeadlines.Gateway.csproj HappyHeadlines.Gateway/
RUN dotnet restore HappyHeadlines.Gateway/HappyHeadlines.Gateway.csproj

COPY HappyHeadlines.Observability/ HappyHeadlines.Observability/
COPY HappyHeadlines.Gateway/ HappyHeadlines.Gateway/
RUN dotnet publish HappyHeadlines.Gateway/HappyHeadlines.Gateway.csproj -c Release -o /app --no-restore -p:DebugType=None -p:DebugSymbols=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app
COPY --from=build /app ./
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "HappyHeadlines.Gateway.dll"]
