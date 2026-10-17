FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Grafana-Monitor.csproj .
RUN dotnet restore Grafana-Monitor.csproj
COPY . .
RUN dotnet publish Grafana-Monitor.csproj -c Release -o /app/publish /p:UseAppHost=false --no-restore

FROM runtime AS final
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "Grafana-Monitor.dll"]
