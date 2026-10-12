$ErrorActionPreference = 'Stop'
foreach ($rid in 'win-x64', 'linux-x64') {
    dotnet publish "$PSScriptRoot\Punk\Punk.csproj" -c Release -r $rid --self-contained `
        -p:PublishSingleFile=true -o "$PSScriptRoot\dist\$rid"
}
