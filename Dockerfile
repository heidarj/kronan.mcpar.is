FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json ./
COPY src/Kronan.mcpar.is/Kronan.mcpar.is.csproj src/Kronan.mcpar.is/
RUN dotnet restore src/Kronan.mcpar.is/Kronan.mcpar.is.csproj
COPY src/ src/
RUN dotnet publish src/Kronan.mcpar.is/Kronan.mcpar.is.csproj -c Release --no-restore -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
COPY --from=build /app .
ENTRYPOINT ["dotnet", "Kronan.mcpar.is.dll"]
