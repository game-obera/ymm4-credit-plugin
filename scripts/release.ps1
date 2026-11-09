#Requires -Version 7
<#
.SYNOPSIS
    配布用パッケージ(.ymme / .zip)を作り、タグを付けて GitHub Release を作成する。

.DESCRIPTION
    手順:
      1. 事前チェック(-DryRun のときは省略): main ブランチ・作業ツリーがクリーン・origin と同期済み・同名タグなし・gh ログイン済み
      2. テストを実行する
      3. Release でビルドする(YMM4 のプラグインフォルダには配置しない)
      4. dist\ に YMM4CreditPlugin-v<バージョン>.ymme と .zip を作る
         中身は「YMM4CreditPlugin\(DLL 2 つと Readme.txt)」。YMM4 のインストーラーは
         zip の一番上のフォルダ名をプラグインのフォルダ名に使うため、フォルダに入れておく。
         登録データ(credit-data.json)は含めない。
      5. GitHub Release を作成して 4 を添付する(-DryRun のときは行わない)。
         タグ v<バージョン> は gh が現在のコミットに作る(リリース作成に失敗したときにタグだけ残らないように)

    ビルドには YMM4 本体の DLL が必要なため、環境変数 YMM4_PATH を設定しておくこと。

.PARAMETER Version
    リリースするバージョン(例: 1.0.0)。タグは v を付けた v1.0.0 になる。

.PARAMETER DryRun
    配布物を作るだけで、タグとリリースは作らない。

.EXAMPLE
    ./scripts/release.ps1 -Version 1.0.0 -DryRun
    ./scripts/release.ps1 -Version 1.0.0
#>
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

$pluginName = 'YMM4CreditPlugin'
$tag = "v$Version"
$repoRoot = Split-Path -Parent $PSScriptRoot
$pluginProject = Join-Path $repoRoot "src\$pluginName\$pluginName.csproj"
$testProject = Join-Path $repoRoot "tests\$pluginName.Core.Tests\$pluginName.Core.Tests.csproj"
$distDir = Join-Path $repoRoot 'dist'
# パッケージに入れるファイル(YMM4 に同梱の DLL は含めない)
$packageDlls = @("$pluginName.dll", "$pluginName.Core.dll")

# 外部コマンドを実行し、失敗したら止める
function Invoke-Checked {
    param([string]$Command, [string[]]$Arguments)
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Command $($Arguments -join ' ') が失敗しました(終了コード $LASTEXITCODE)" }
}

if (-not $env:YMM4_PATH -or -not (Test-Path (Join-Path $env:YMM4_PATH 'YukkuriMovieMaker.Plugin.dll'))) {
    throw '環境変数 YMM4_PATH に YMM4 のフォルダを設定してください'
}

Push-Location $repoRoot
try {
    # ---- 1. 事前チェック ----
    if (-not $DryRun) {
        Write-Host '事前チェックをしています…'
        if (git status --porcelain) { throw '作業ツリーに未コミットの変更があります' }
        $branch = git branch --show-current
        if ($branch -ne 'main') { throw "main ブランチで実行してください(現在: $branch)" }
        Invoke-Checked git @('fetch', '--quiet', '--tags', 'origin')
        if ((git rev-parse HEAD) -ne (git rev-parse origin/main)) { throw 'main が origin/main と一致していません。push または pull してください' }
        if (git tag --list $tag) { throw "タグ $tag は既にあります" }
        Invoke-Checked gh @('auth', 'status')
    }

    # ---- 2. テスト ----
    # 配布するのと同じ Release 構成でテストする
    Write-Host 'テストを実行しています…'
    Invoke-Checked dotnet @('test', $testProject, '-c', 'Release', '-nologo', '-v', 'q')

    $work = Join-Path ([IO.Path]::GetTempPath()) "$pluginName-release-$([Guid]::NewGuid().ToString('N'))"
    try {
        # ---- 3. ビルド ----
        Write-Host "Release でビルドしています(バージョン $Version)…"
        $binDir = Join-Path $work 'bin'
        # ContinuousIntegrationBuild: DLL に埋め込まれるパスを /_/ に置き換え、ビルドした PC のパス(ユーザー名など)を残さない
        Invoke-Checked dotnet @('build', $pluginProject, '-c', 'Release', '-nologo', '-v', 'q',
            "-p:Version=$Version", '-p:SkipPluginDeploy=true', '-p:ContinuousIntegrationBuild=true', '-o', $binDir)

        # ---- 4. パッケージ作成 ----
        $stageDir = Join-Path $work "stage\$pluginName"
        New-Item -ItemType Directory -Force -Path $stageDir | Out-Null
        foreach ($dll in $packageDlls) {
            Copy-Item -LiteralPath (Join-Path $binDir $dll) -Destination $stageDir
        }

        # Readme.txt は YMM4 のインストーラーに表示される。テンプレートに LICENSE を付けて、UTF-8(BOM 付き)・CRLF で書く
        $readme = (Get-Content -LiteralPath (Join-Path $repoRoot 'packaging\Readme.txt') -Raw -Encoding UTF8).Replace('{VERSION}', $Version) +
            "`n" + ('=' * 60) + "`n`n" + (Get-Content -LiteralPath (Join-Path $repoRoot 'LICENSE') -Raw -Encoding UTF8)
        $readme = $readme -replace "`r?`n", "`r`n"
        [IO.File]::WriteAllText((Join-Path $stageDir 'Readme.txt'), $readme, [Text.UTF8Encoding]::new($true))

        if (Test-Path $distDir) { Remove-Item -LiteralPath $distDir -Recurse -Force }
        New-Item -ItemType Directory -Path $distDir | Out-Null
        $zipPath = Join-Path $distDir "$pluginName-$tag.zip"
        $ymmePath = Join-Path $distDir "$pluginName-$tag.ymme"
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        # includeBaseDirectory = true で、zip の一番上を YMM4CreditPlugin\ フォルダにする
        [IO.Compression.ZipFile]::CreateFromDirectory($stageDir, $zipPath, [IO.Compression.CompressionLevel]::Optimal, $true)
        Copy-Item -LiteralPath $zipPath -Destination $ymmePath
    }
    finally {
        if (Test-Path $work) { Remove-Item -LiteralPath $work -Recurse -Force }
    }

    Write-Host "配布物を作成しました:`n  $ymmePath`n  $zipPath"
    if ($DryRun) {
        Write-Host '-DryRun のため、タグとリリースは作成しません'
        return
    }

    # ---- 5. リリース(タグも同時に作る) ----
    Write-Host "GitHub Release $tag を作成しています…"
    $commit = git rev-parse HEAD
    Invoke-Checked gh @('release', 'create', $tag, $ymmePath, $zipPath, '--target', $commit, '--title', $tag, '--generate-notes')
    Invoke-Checked git @('fetch', '--quiet', '--tags', 'origin')
    Write-Host "リリースしました: $tag"
}
finally {
    Pop-Location
}
