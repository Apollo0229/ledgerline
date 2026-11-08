# Builds the API and the seeder from one restore. Targets: `api` and `seeder`.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props .editorconfig ./
COPY src/Ledgerline.Core/Ledgerline.Core.csproj src/Ledgerline.Core/
COPY src/Ledgerline.Search/Ledgerline.Search.csproj src/Ledgerline.Search/
COPY src/Ledgerline.Api/Ledgerline.Api.csproj src/Ledgerline.Api/
COPY src/Ledgerline.Seeder/Ledgerline.Seeder.csproj src/Ledgerline.Seeder/
RUN dotnet restore src/Ledgerline.Api && dotnet restore src/Ledgerline.Seeder
COPY src/ src/
RUN dotnet publish src/Ledgerline.Api -c Release -o /out/api --no-restore \
 && dotnet publish src/Ledgerline.Seeder -c Release -o /out/seeder --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS seeder
WORKDIR /app
COPY --from=build /out/seeder .
USER $APP_UID
ENTRYPOINT ["dotnet", "Ledgerline.Seeder.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS api
WORKDIR /app
COPY --from=build /out/api .
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Ledgerline.Api.dll"]
