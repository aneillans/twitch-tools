FROM mcr.microsoft.com/dotnet/sdk:10.0.300 AS build
WORKDIR /src

COPY TwitchTools.slnx ./
COPY dotnet-tools.json ./
COPY src/TwitchTools.Web/TwitchTools.Web.csproj src/TwitchTools.Web/
RUN dotnet restore src/TwitchTools.Web/TwitchTools.Web.csproj

COPY . .
RUN dotnet publish src/TwitchTools.Web/TwitchTools.Web.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
RUN apt-get update \
    && apt-get upgrade -y \
	&& apt-get install -y --no-install-recommends libgssapi-krb5-2 \
	&& rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "TwitchTools.Web.dll"]
