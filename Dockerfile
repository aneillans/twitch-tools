FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

COPY TwitchTools.slnx ./
COPY dotnet-tools.json ./
COPY src/TwitchTools.Web/TwitchTools.Web.csproj src/TwitchTools.Web/
RUN dotnet restore src/TwitchTools.Web/TwitchTools.Web.csproj

COPY . .
RUN dotnet publish src/TwitchTools.Web/TwitchTools.Web.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app
RUN apk update \
    && apk upgrade \
	&& apk add --no-cache krb5-libs \
	&& rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "TwitchTools.Web.dll"]
