rm *.nupkg
rm -rf obj/*
rm -rf Debug/*
rm -rf Release/*
set -e
msbuild dxmppnet.csproj /p:Configuration=Release /p:TargetFramework=v4.0
nuget pack dxmppnet.nuspec -properties Configuration:Release

