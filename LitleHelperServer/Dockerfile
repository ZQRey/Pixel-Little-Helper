FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY LitleHelperServer.csproj ./
RUN dotnet restore
COPY . ./
RUN dotnet publish -c Release -o /app/publish --no-restore
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
RUN apt-get update && apt-get install -y --no-install-recommends curl libgssapi-krb5-2 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish ./
RUN mkdir -p /app/data && chown -R app:app /app
USER app
ENV ASPNETCORE_HTTP_PORTS=5000
ENV KRB5_CONFIG=/app/data/kerberos/krb5.conf KRB5_KTNAME=/app/data/kerberos/helper.keytab
EXPOSE 5000
ENTRYPOINT ["dotnet", "LitleHelperServer.dll"]
