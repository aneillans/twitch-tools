FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

ENV DOTNET_SYSTEM_NET_HTTP_SOCKETSHTTPHANDLER_HTTP2SUPPORT=false

COPY TwitchTools.slnx ./
COPY dotnet-tools.json ./
COPY Directory.Packages.props ./
COPY Directory.Build.props ./
COPY src/TwitchTools.Web/TwitchTools.Web.csproj src/TwitchTools.Web/
COPY src/TwitchTools.Web/nuget.config src/TwitchTools.Web/
RUN dotnet restore src/TwitchTools.Web/TwitchTools.Web.csproj --configfile src/TwitchTools.Web/nuget.config

COPY . .
RUN dotnet publish src/TwitchTools.Web/TwitchTools.Web.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app
RUN apk update \
	&& apk upgrade \
	&& apk add --no-cache krb5-libs \
	&& rm -rf /var/cache/apk/*
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "TwitchTools.Web.dll"]
