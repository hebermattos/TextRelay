FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore src/TextRelay.Worker/TextRelay.Worker.csproj
RUN dotnet publish src/TextRelay.Worker/TextRelay.Worker.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet","TextRelay.Worker.dll"]
