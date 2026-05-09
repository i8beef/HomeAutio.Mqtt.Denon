FROM mcr.microsoft.com/dotnet/runtime:8.0-alpine AS runtime

WORKDIR /app

COPY . ./

ENV ENVIRONMENT=Production

VOLUME ["/app/appsettings.Production.json", "/app/logs"]

ENTRYPOINT ["dotnet", "HomeAutio.Mqtt.Denon.dll"]