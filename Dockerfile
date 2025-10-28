# Build stage =========================================================================
FROM mcr.microsoft.com/dotnet/core/sdk:3.1 as builder

COPY . /src

#===== Build MAS/MLS/NAT
WORKDIR /src/server/horizon-server/Server.Medius
RUN dotnet publish -c Release -o /server

RUN cp /server/*.dll /src/Horizon.Plugin.Deadlocked/

#====== Build Plugin
WORKDIR /src/Horizon.Plugin.Deadlocked
RUN dotnet publish -c Release -o /out/medius

CMD "/src/entrypoint.sh"
