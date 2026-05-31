FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["src/Kronan.mcpar.is/Kronan.mcpar.is.csproj", "src/Kronan.mcpar.is/"]
RUN dotnet restore "src/Kronan.mcpar.is/Kronan.mcpar.is.csproj"
COPY . .
WORKDIR "/src/src/Kronan.mcpar.is"
RUN dotnet build "Kronan.mcpar.is.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Kronan.mcpar.is.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Kronan.mcpar.is.dll"]
