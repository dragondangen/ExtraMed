FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ExtraMed.csproj ./
RUN dotnet restore ExtraMed.csproj
COPY *.cs ./
COPY appsettings.json ./
COPY Views/ ./Views/
COPY wwwroot/ ./wwwroot/
RUN dotnet publish ExtraMed.csproj --no-restore -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out/ ./
ENV ExtraMed__UseVercel=true ASPNETCORE_HTTP_PORTS=80
EXPOSE 80
ENTRYPOINT ["dotnet", "ExtraMed.dll"]
