# Stage 1: build the app using the .NET SDK image
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /code
# Copy only the project file first so dotnet restore is cached
# separately from the source code (re-runs only when deps change)
COPY src/TodoMcp.csproj src/TodoMcp.csproj
# Download NuGet packages referenced by the csproj
RUN dotnet restore src/TodoMcp.csproj
# Now copy the full source code
COPY . .
# Git version (tag/sha) reported by the get_version tool; .git is not in the build context
ARG GIT_VERSION=dev
# Compile and publish a Release build to /app/publish;
# UseAppHost=false skips the apphost native binary (we launch via `dotnet`)
RUN dotnet publish src/TodoMcp.csproj -c Release -o /app/publish /p:UseAppHost=false /p:InformationalVersion=${GIT_VERSION}

# Stage 2: lean runtime image (no SDK, no compiler)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# Install curl and clean up the apt lists to keep the image small
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .
# Bind the server to all interfaces on port 8080
ENV ASPNETCORE_URLS=http://+:8080
# Tell ASP.NET this is a production run (production logging, etc.)
ENV DOTNET_ENVIRONMENT=Production
# Document the port the container listens on (metadata for docker)
EXPOSE 8080
# Start the app: run the published Managed DLL via dotnet
ENTRYPOINT ["dotnet", "TodoMcp.dll"]
