# Production images — two targets sharing one build stage:
#   api        : the ASP.NET Core application
#   migrations : the self-contained EF Core migration bundle, run as a
#                one-shot job before the api starts (compose: depends_on
#                service_completed_successfully)
FROM mcr.microsoft.com/dotnet/sdk@sha256:ea8bde36c11b6e7eec2656d0e59101d4462f6bd630730f2c8201ed0572b295d5 AS build

WORKDIR /src
COPY VerbatimIntelligence.slnx Directory.Build.props dotnet-tools.json ./
COPY src/VerbatimIntelligence.Api/VerbatimIntelligence.Api.csproj src/VerbatimIntelligence.Api/
COPY tests/Directory.Build.props tests/VerbatimIntelligence.Api.Tests/VerbatimIntelligence.Api.Tests.csproj tests/VerbatimIntelligence.Api.Tests/
RUN dotnet restore VerbatimIntelligence.slnx
COPY . .
RUN dotnet publish src/VerbatimIntelligence.Api -c Release -o /out/api
# The design-time host builds the real Program, whose guard requires a
# connection string; EF never connects while bundling, any value satisfies it.
RUN dotnet tool restore \
    && ConnectionStrings__Database="Host=design-time" \
       ConnectionStrings__Redis="design-time" \
       dotnet ef migrations bundle --project src/VerbatimIntelligence.Api \
       --configuration Release --self-contained -r linux-musl-x64 -o /out/efbundle

FROM mcr.microsoft.com/dotnet/aspnet@sha256:57bd717ac18ff6c8a39cc0ee4a76c1f15adc46df50434c73eff0c3f1df4c88f0 AS api

WORKDIR /app
COPY --from=build /out/api .
USER app
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=3s --start-period=15s \
  CMD wget -qO /dev/null http://127.0.0.1:8080/health || exit 1

ENTRYPOINT ["dotnet", "VerbatimIntelligence.Api.dll"]

FROM mcr.microsoft.com/dotnet/aspnet@sha256:57bd717ac18ff6c8a39cc0ee4a76c1f15adc46df50434c73eff0c3f1df4c88f0 AS migrations

WORKDIR /app
COPY --from=build /out/efbundle .
USER app

# HEALTHCHECK does not apply: one-shot job, its exit code is the signal.
HEALTHCHECK NONE

ENTRYPOINT ["sh", "-c", "./efbundle --connection \"$ConnectionStrings__Database\""]
