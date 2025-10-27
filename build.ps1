Remove-Item -Recurse -Force ./out
Remove-Item -Recurse -Force ./server
Remove-Item -Recurse -Force ./out/medius
mkdir ./out/medius
mkdir ./server

#cp ../horizon-server server -r
Copy-Item ../horizon-server -Destination ./server -Force -Recurse

docker build . -t deadlocked_plugin

docker run -v ${PWD}/out/:/mnt/out -it deadlocked_plugin

sudo chmod a+rw out -R

