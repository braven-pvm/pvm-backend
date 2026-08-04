FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY backend/Pvm.sln backend/Pvm.sln
COPY backend/src/Pvm.Domain/Pvm.Domain.csproj backend/src/Pvm.Domain/
COPY backend/src/Pvm.Application/Pvm.Application.csproj backend/src/Pvm.Application/
COPY backend/src/Pvm.Infrastructure/Pvm.Infrastructure.csproj backend/src/Pvm.Infrastructure/
COPY backend/src/Pvm.Worker/Pvm.Worker.csproj backend/src/Pvm.Worker/

RUN dotnet restore backend/src/Pvm.Worker/Pvm.Worker.csproj

COPY backend/src backend/src
RUN dotnet publish backend/src/Pvm.Worker/Pvm.Worker.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "Pvm.Worker.dll"]
