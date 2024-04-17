FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS base
WORKDIR /app

# Install OpenSSL if not installed (Uncomment the following line if needed)
# RUN apt-get update && apt-get install -y openssl

# Check if openssl.cnf exists, and modify it or create a new one
RUN if [ -f /etc/ssl/openssl.cnf ]; then \
    sed -i 's/@SECLEVEL=2/@SECLEVEL=1/g' /etc/ssl/openssl.cnf; \
else \
    echo "[system_default_sect]" >> /etc/ssl/openssl.cnf; \
    echo "MinProtocol = TLSv1.2" >> /etc/ssl/openssl.cnf; \
    echo "CipherString = DEFAULT:@SECLEVEL=1" >> /etc/ssl/openssl.cnf; \
fi

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
# Copy csproj and restore as distinct layers
COPY ["MyApp/MyApp.csproj", "MyApp/"]
RUN dotnet restore "MyApp/MyApp.csproj"
COPY . .
WORKDIR "/src/MyApp"
RUN dotnet build "MyApp.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "MyApp.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "MyApp.dll"]
