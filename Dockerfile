FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project files
COPY src/InventoryBot.Domain/*.csproj ./InventoryBot.Domain/
COPY src/InventoryBot.Application/*.csproj ./InventoryBot.Application/
COPY src/InventoryBot.Infrastructure/*.csproj ./InventoryBot.Infrastructure/
COPY src/InventoryBot.Worker/*.csproj ./InventoryBot.Worker/

# Restore dependencies
RUN dotnet restore ./InventoryBot.Worker/InventoryBot.Worker.csproj

# Copy source code
COPY src/ ./

# Build and Publish
RUN dotnet publish ./InventoryBot.Worker/InventoryBot.Worker.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "InventoryBot.Worker.dll"]
