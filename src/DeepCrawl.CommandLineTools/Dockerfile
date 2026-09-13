FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/DeepCrawl.CommandLineTools/DeepCrawl.CommandLineTools.csproj", "src/DeepCrawl.CommandLineTools/"]
COPY ["src/DeepCrawl.Domain/DeepCrawl.Domain.csproj", "src/DeepCrawl.Domain/"]
COPY ["Directory.Build.props", "."]
COPY ["Directory.Packages.props", "."]
RUN dotnet restore "src/DeepCrawl.CommandLineTools/DeepCrawl.CommandLineTools.csproj"
COPY . .
WORKDIR "/src/src/DeepCrawl.CommandLineTools"
RUN dotnet publish "DeepCrawl.CommandLineTools.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "DeepCrawl.CommandLineTools.dll"]
