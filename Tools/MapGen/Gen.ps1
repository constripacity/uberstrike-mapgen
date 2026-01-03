
param(
  [int]$seed = 1337,
  [int]$size = 96,
  [int]$t = 2,
  [string]$unity = "$env:ProgramFiles\Unity\Hub\Editor\6000.0.56f1\Editor\Unity.exe",
  [string]$project = "$PSScriptRoot\..\..\"
)
& $unity -batchmode -quit `
  -projectPath (Resolve-Path $project) `
  -executeMethod MapGen.CLI.Run `
  -seed $seed -size $size -t $t

