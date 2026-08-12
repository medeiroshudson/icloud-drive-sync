FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ICloudDriveSync.slnx ./
COPY src/ICloudDriveSync/ICloudDriveSync.csproj src/ICloudDriveSync/
COPY tests/ICloudDriveSync.Tests/ICloudDriveSync.Tests.csproj tests/ICloudDriveSync.Tests/

RUN dotnet restore ICloudDriveSync.slnx

COPY . .

RUN dotnet publish src/ICloudDriveSync/ICloudDriveSync.csproj -c Release -o /app/out --no-restore

# CLI puro (sem ASP.NET) — runtime simples
FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app

ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 \
    DOTNET_EnableDiagnostics=0

COPY --from=build /app/out .

ENTRYPOINT ["dotnet", "ICloudDriveSync.dll"]