# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj and restore as distinct layers
COPY src/PaytmReservationSystem/PaytmReservationSystem.csproj PaytmReservationSystem/
RUN dotnet restore PaytmReservationSystem/PaytmReservationSystem.csproj

# Copy everything else and build
COPY src/ PaytmReservationSystem/
WORKDIR /src/PaytmReservationSystem
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Expose port and set environment variables
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "PaytmReservationSystem.dll"]