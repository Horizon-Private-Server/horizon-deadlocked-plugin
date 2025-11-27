# Build stage =========================================================================
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS builder

COPY . /src

#===== Build MAS/MLS/NAT
WORKDIR /src/server/horizon-server/Server.Medius
RUN dotnet publish -c Release -o /server

RUN cp /server/*.dll /src/Horizon.Plugin.Deadlocked/

#====== Build Plugin
WORKDIR /src/Horizon.Plugin.Deadlocked
RUN dotnet publish -c Release -o /out/medius

CMD "/src/entrypoint.sh"
