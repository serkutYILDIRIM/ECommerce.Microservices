FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy csproj files and restore dependencies
COPY ["src/OrderProcessingService/OrderProcessingService.csproj", "src/OrderProcessingService/"]
COPY ["src/Shared.Library/Shared.Library.csproj", "src/Shared.Library/"]
RUN dotnet restore "src/OrderProcessingService/OrderProcessingService.csproj"

# Copy all files and build
COPY . .
RUN dotnet build "src/OrderProcessingService/OrderProcessingService.csproj" -c Release -o /app/build

# Publish the application
FROM build AS publish
RUN dotnet publish "src/OrderProcessingService/OrderProcessingService.csproj" -c Release -o /app/publish

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .
EXPOSE 80
EXPOSE 443
ENTRYPOINT ["dotnet", "OrderProcessingService.dll"]
