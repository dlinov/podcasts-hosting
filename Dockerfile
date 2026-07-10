FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY . .

RUN dotnet restore PodcastsHosting.slnx
RUN dotnet tool restore
RUN cd PodcastsHosting && dotnet tool run libman -- restore --verbosity minimal
RUN dotnet publish PodcastsHosting/PodcastsHosting.csproj --configuration Release --no-restore --output /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish .

USER $APP_UID

ENTRYPOINT ["dotnet", "PodcastsHosting.dll"]