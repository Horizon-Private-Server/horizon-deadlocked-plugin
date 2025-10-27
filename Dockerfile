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

RUN mv /out/medius/runtimes/linux-x64/native/SQLite.Interop.dll /out/medius/
RUN rm -rf /out/medius/runtimes/osx-x64  
RUN rm -rf /out/medius/runtimes/win-x64  
RUN rm -rf /out/medius/runtimes/unix 
RUN rm -rf /out/medius/runtimes/win-x86 
RUN rm -rf /out/medius/runtimes/win-arm64  
RUN rm -rf /out/medius/runtimes/win
RUN rm -rf /out/medius/runtimes

CMD "/src/entrypoint.sh"
