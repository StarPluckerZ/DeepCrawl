FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/DeepCrawl.Api/DeepCrawl.Api.csproj", "src/DeepCrawl.Api/"]
COPY ["src/DeepCrawl.Core/DeepCrawl.Core.csproj", "src/DeepCrawl.Core/"]
COPY ["src/DeepCrawl.Domain/DeepCrawl.Domain.csproj", "src/DeepCrawl.Domain/"]
COPY ["src/DeepCrawl.Infrastructure/DeepCrawl.Infrastructure.csproj", "src/DeepCrawl.Infrastructure/"]
COPY ["Directory.Build.props", "."]
COPY ["Directory.Packages.props", "."]
RUN dotnet restore "src/DeepCrawl.Api/DeepCrawl.Api.csproj"
COPY . .
WORKDIR "/src/src/DeepCrawl.Api"
RUN dotnet build "DeepCrawl.Api.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "DeepCrawl.Api.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
RUN mkdir /app/logs
ENV ASPNETCORE_ENVIRONMENT=Docker
ENTRYPOINT ["dotnet", "DeepCrawl.Api.dll"]
