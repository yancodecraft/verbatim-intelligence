# Production images — two targets sharing one build stage:
#   api        : the ASP.NET Core application
#   migrations : the self-contained EF Core migration bundle, run as a
#                one-shot job before the api starts (compose: depends_on
#                service_completed_successfully)
FROM mcr.microsoft.com/dotnet/sdk@sha256:940f919ae84dd92ccd4aab7686fa5b777870b006c9360351039e16bcaad73d89 AS build

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

FROM mcr.microsoft.com/dotnet/aspnet@sha256:7644f992230d35cf230017189d4038c0ae0f7388b13f4f7ae1900a155bafb597 AS api

WORKDIR /app
COPY --from=build /out/api .
USER app
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=3s --start-period=15s \
  CMD wget -qO /dev/null http://127.0.0.1:8080/health || exit 1

ENTRYPOINT ["dotnet", "VerbatimIntelligence.Api.dll"]

FROM mcr.microsoft.com/dotnet/aspnet@sha256:7644f992230d35cf230017189d4038c0ae0f7388b13f4f7ae1900a155bafb597 AS migrations

WORKDIR /app
COPY --from=build /out/efbundle .
USER app

# HEALTHCHECK does not apply: one-shot job, its exit code is the signal.
HEALTHCHECK NONE

ENTRYPOINT ["sh", "-c", "./efbundle --connection \"$ConnectionStrings__Database\""]
