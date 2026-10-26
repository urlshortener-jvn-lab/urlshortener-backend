# Stage 1: Base runtime image (chỉ chứa ASP.NET Core Runtime nhẹ & bảo mật, chạy bằng non-root user $APP_UID)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080

# Stage 2: SDK image để restore và build (tận dụng Docker Layer Caching)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy các file quản lý gói tập trung và csproj trước -> Nếu chỉ sửa code .cs thì Docker KHÔNG phải chạy lại dotnet restore!
COPY ["Directory.Packages.props", "."]
COPY ["Directory.Build.props", "."]
COPY ["nuget.config", "."]

COPY ["src/UrlShortener.Domain/UrlShortener.Domain.csproj", "src/UrlShortener.Domain/"]
COPY ["src/UrlShortener.Application/UrlShortener.Application.csproj", "src/UrlShortener.Application/"]
COPY ["src/UrlShortener.Infrastructure/UrlShortener.Infrastructure.csproj", "src/UrlShortener.Infrastructure/"]
COPY ["src/UrlShortener.ServiceDefaults/UrlShortener.ServiceDefaults.csproj", "src/UrlShortener.ServiceDefaults/"]
COPY ["src/UrlShortener.API/UrlShortener.API.csproj", "src/UrlShortener.API/"]

RUN dotnet restore "src/UrlShortener.API/UrlShortener.API.csproj"

# Copy toàn bộ source code còn lại vào container
COPY . .

WORKDIR "/src/src/UrlShortener.API"

# Stage 3: Publish ra các file thực thi tối ưu
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "UrlShortener.API.csproj" \
    -c $BUILD_CONFIGURATION \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# Stage 4: Final image (chỉ lấy thư mục publish ghép vào base runtime)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "UrlShortener.API.dll"]
