FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files
COPY NuGet.config ./
COPY src/InventoryBot.Domain/*.csproj ./InventoryBot.Domain/
COPY src/InventoryBot.Application/*.csproj ./InventoryBot.Application/
COPY src/InventoryBot.Infrastructure/*.csproj ./InventoryBot.Infrastructure/
COPY src/InventoryBot.Worker/*.csproj ./InventoryBot.Worker/

# Restore dependencies
ENV NUGET_HTTP_TIMEOUT=300
RUN /bin/bash -c 'set -e; for i in {1..3}; do dotnet restore ./InventoryBot.Worker/InventoryBot.Worker.csproj --disable-parallel && exit 0; echo "Restore failed, retrying in 5s..." >&2; sleep 5; done; exit 1'

# Copy source code
COPY src/ ./

# Build and Publish
RUN /bin/bash -c 'set -e; for i in {1..3}; do dotnet publish ./InventoryBot.Worker/InventoryBot.Worker.csproj -c Release -o /app/publish --no-restore && exit 0; echo "Publish failed, rerunning restore..." >&2; dotnet restore ./InventoryBot.Worker/InventoryBot.Worker.csproj --disable-parallel || true; sleep 5; done; exit 1'

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "InventoryBot.Worker.dll"]
