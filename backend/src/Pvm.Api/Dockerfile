FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY backend/Pvm.sln backend/Pvm.sln
COPY backend/src/Pvm.Domain/Pvm.Domain.csproj backend/src/Pvm.Domain/
COPY backend/src/Pvm.Application/Pvm.Application.csproj backend/src/Pvm.Application/
COPY backend/src/Pvm.Infrastructure/Pvm.Infrastructure.csproj backend/src/Pvm.Infrastructure/
COPY backend/src/Pvm.Api/Pvm.Api.csproj backend/src/Pvm.Api/

RUN dotnet restore backend/src/Pvm.Api/Pvm.Api.csproj

COPY backend/src backend/src
RUN dotnet publish backend/src/Pvm.Api/Pvm.Api.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Pvm.Api.dll"]
