#!/bin/bash
set -e

sudo rm -rf out
sudo rm -rf server
sudo rm -rf out/medius
mkdir -p out/medius
mkdir -p server

cp ../horizon-server server -r

docker build . -t deadlocked_plugin

docker run -v "${PWD}/out/":/mnt/out -it deadlocked_plugin

sudo chmod a+rw out -R

