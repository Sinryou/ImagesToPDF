<#
.SYNOPSIS
    ImgsToPDF 构建与打包脚本

.DESCRIPTION
    自动化执行依赖还原、项目编译、文件收集、绿色免安装 Zip 归档以及可选的 Inno Setup 安装包制作。

.PARAMETER Configuration
    构建配置，默认为 Release，可选 Debug。

.PARAMETER Platform
    目标平台架构，默认为 x64。

.PARAMETER Clean
    清理以往的构建和打包产物。

.PARAMETER Package
    打包为便携版 ZIP 压缩包并生成 SHA256 校验和。

.PARAMETER Installer
    调用 Inno Setup 制作 Windows 安装包（需要安装 Inno Setup 6）。

.PARAMETER Version
    指定版本号（如 1.3.0）。若未指定，自动从 ImgsToPDF/Properties/AssemblyInfo.cs 中读取。

.PARAMETER OutputDir
    打包归档产物的输出目录，默认为项目根目录下的 artifacts。

.EXAMPLE
    .\build\build.ps1
    仅编译 Release|x64。

.EXAMPLE
    .\build\build.ps1 -Clean -Package
    清理并重新编译，同时打包便携版 ZIP。

.EXAMPLE
    .\build\build.ps1 -Package -Installer
    打包便携版 ZIP 并制作 Inno Setup 安装程序。
#>

[CmdletBinding()]
param(
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release",

    [ValidateSet("x64")]
    [string]$Platform = "x64",

    [switch]$Clean,

    [switch]$Package,

    [switch]$Installer,

    [string]$Version,

    [string]$OutputDir
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# 定位仓库根目录与构建目录
if ($PSScriptRoot) {
    $scriptDir = $PSScriptRoot
} elseif ($MyInvocation.MyCommand -and $MyInvocation.MyCommand.Path) {
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
} else {
    $scriptDir = (Get-Location).Path
}

if (Test-Path (Join-Path $scriptDir "..\ImgsToPDF.slnx")) {
    $repoRoot = (Resolve-Path (Join-Path $scriptDir "..")).Path
} elseif (Test-Path (Join-Path $scriptDir "ImgsToPDF.slnx")) {
    $repoRoot = (Resolve-Path $scriptDir).Path
} else {
    $repoRoot = (Get-Location).Path
}
if (-not $OutputDir) {
    $OutputDir = Join-Path $repoRoot "artifacts"
} else {
    if (-not [System.IO.Path]::IsPathRooted($OutputDir)) {
        $OutputDir = Join-Path $repoRoot $OutputDir
    }
}

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "             ImgsToPDF 构建与打包管线                      " -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "仓库目录: $repoRoot"
Write-Host "构建配置: $Configuration | $Platform"

# 1. 解析版本号
if (-not $Version) {
    $assemblyInfoPath = Join-Path $repoRoot "ImgsToPDF\Properties\AssemblyInfo.cs"
    if (Test-Path $assemblyInfoPath) {
        $content = Get-Content $assemblyInfoPath -Raw
        if ($content -match '\[assembly:\s*AssemblyFileVersion\("([^"]+)"\)\]') {
            $Version = $matches[1]
        } elseif ($content -match '\[assembly:\s*AssemblyVersion\("([^"]+)"\)\]') {
            $Version = $matches[1]
        }
    }
    if (-not $Version) {
        $Version = "1.0.0"
    }
}
Write-Host "应用版本: v$Version" -ForegroundColor Green

# 2. 寻找 MSBuild.exe
function Find-MSBuild {
    # 2.1 检查 PATH
    $cmd = Get-Command msbuild.exe -ErrorAction SilentlyContinue
    if ($cmd) {
        return $cmd.Source
    }

    # 2.2 使用 vswhere 探测
    $vswhereCandidates = @(
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\Installer\vswhere.exe"
    )
    foreach ($vswhere in $vswhereCandidates) {
        if (Test-Path $vswhere) {
            $path = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
            if ($path -and (Test-Path $path)) {
                return $path
            }
        }
    }

    # 2.3 探测常见默认安装路径（支持多盘符）
    $drives = @("C:", "D:", "E:")
    $vsEditions = @("Community", "Professional", "Enterprise", "BuildTools")
    $vsYears = @("18", "2022", "2019")
    foreach ($drive in $drives) {
        foreach ($year in $vsYears) {
            foreach ($edition in $vsEditions) {
                $candidates = @(
                    "$drive\Program Files\Microsoft Visual Studio\$year\$edition\MSBuild\Current\Bin\MSBuild.exe",
                    "$drive\Program Files (x86)\Microsoft Visual Studio\$year\$edition\MSBuild\Current\Bin\MSBuild.exe",
                    "$drive\Development\Microsoft Visual Studio\$year\$edition\MSBuild\Current\Bin\MSBuild.exe"
                )
                foreach ($c in $candidates) {
                    if (Test-Path $c) {
                        return $c
                    }
                }
            }
        }
    }

    throw "未找到 MSBuild.exe。请确保已安装 Visual Studio 或 Visual Studio Build Tools，并安装了 MSBuild 组件。"
}

$msbuildPath = Find-MSBuild
Write-Host "使用 MSBuild: $msbuildPath" -ForegroundColor Gray

# 3. 解决 Windows 环境下的 Mark of the Web (Zone.Identifier / Integrity Level)
# 只处理 .resx：GenerateResource 报 MSB3821 的触发条件是「.resx 带有网络来源标记，
# 或所在目录/文件处于 Low 完整性级别」。因此只需修复真正会触发它的那几个文件，
# 不要对整个仓库（含 .git）递归改写完整性级别——那既慢，又会在工作副本上留下
# 与版本控制无关的 ACL 副作用，而且遍历期间任何被占用的文件都会让整条命令中途失败。
try {
    $hasIcacls = $null -ne (Get-Command icacls.exe -ErrorAction SilentlyContinue)

    # 先定位项目目录（含工程文件的目录），再只在其中找 .resx——
    # 避免为了找几个 resx 而遍历 .git、bin、obj 等与构建无关的内容。
    $projDirs = Get-ChildItem -Path $repoRoot -Filter "*.??proj" -File -Recurse -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty DirectoryName -Unique

    foreach ($projDir in $projDirs) {
        $resxFiles = @(Get-ChildItem -Path $projDir -Filter "*.resx" -Recurse -File -ErrorAction SilentlyContinue)
        foreach ($resx in $resxFiles) {
            # (a) 去掉网络来源标记。等价于 Unblock-File，但只对确实带标记的文件动手，
            #     并且直接删除 Zone.Identifier 流——避免 Unblock-File 在个别文件上失败时中断整批处理。
            try {
                $zoneStream = Get-Item -LiteralPath $resx.FullName -Stream Zone.Identifier -ErrorAction SilentlyContinue
                if ($zoneStream) {
                    Remove-Item -LiteralPath $resx.FullName -Stream Zone.Identifier -ErrorAction Stop
                    Write-Host "  已移除网络来源标记: $($resx.FullName)" -ForegroundColor Gray
                }
            }
            catch {
                Write-Warning "无法移除网络来源标记: $($resx.FullName) - $($_.Exception.Message)"
            }

            # (b) 保证不处于 Low 完整性级别。icacls 的 Mandatory Label 名称会随系统语言本地化
            #     （中文系统是「强制性级别」），解析输出判断不可靠，而该命令本身是幂等的，
            #     所以直接设置。这里刻意不加 /T，作用范围仅限该文件，不波及子目录与仓库其它内容。
            if ($hasIcacls) {
                try {
                    & icacls.exe $resx.FullName /setintegritylevel Medium 2>&1 | Out-Null
                }
                catch {
                    Write-Warning "无法调整完整性级别: $($resx.FullName) - $($_.Exception.Message)"
                }
            }
        }
    }
} catch {
    # 仅作为安全兜底，非致命
}

# 4. 执行 Clean
if ($Clean) {
    Write-Host "`n--> 正在清理旧构建和输出目录..." -ForegroundColor Yellow
    $dirsToClean = @(
        (Join-Path $repoRoot "ImgsToPDF\bin"),
        (Join-Path $repoRoot "ImgsToPDF\obj"),
        (Join-Path $repoRoot "ImgsToPDF\Core"),
        (Join-Path $repoRoot "ImgsToPDFCore\bin"),
        (Join-Path $repoRoot "ImgsToPDFCore\obj"),
        $OutputDir,
        (Join-Path $repoRoot "build\dist")
    )
    foreach ($dir in $dirsToClean) {
        if (Test-Path $dir) {
            Write-Host "  删除: $dir"
            Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

# 5. 执行编译
Write-Host "`n--> 正在编译解决方案 (配置: $Configuration, 平台: $Platform)..." -ForegroundColor Cyan
$solutionPath = Join-Path $repoRoot "ImgsToPDF.slnx"
if (-not (Test-Path $solutionPath)) {
    # 兼容标准 .sln 文件
    $solutionPath = Join-Path $repoRoot "ImgsToPDF.sln"
}
if (-not (Test-Path $solutionPath)) {
    throw "未找到解决方案文件 ($solutionPath)"
}

$buildArgs = @(
    $solutionPath,
    "-restore",
    "/p:Configuration=$Configuration",
    "/p:Platform=$Platform",
    "/nr:false",
    "/p:UseSharedCompilation=false",
    "/v:minimal",
    "/nologo"
)

Write-Host "执行: $msbuildPath $($buildArgs -join ' ')" -ForegroundColor Gray
& $msbuildPath @buildArgs
if ($LASTEXITCODE -ne 0) {
    throw "编译失败，MSBuild 返回代码: $LASTEXITCODE"
}
Write-Host "编译成功！" -ForegroundColor Green

# 6. 如果需要打包
if ($Package -or $Installer) {
    Write-Host "`n--> 正在收集发布文件..." -ForegroundColor Cyan

    $binDir = Join-Path $repoRoot "ImgsToPDF\bin\$Platform\$Configuration"
    if (-not (Test-Path $binDir)) {
        throw "编译输出目录不存在: $binDir"
    }

    # 准备 staging 目录
    $stageDir = Join-Path $repoRoot "build\dist\ImgsToPDF"
    if (Test-Path $stageDir) {
        Remove-Item $stageDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $stageDir -Force | Out-Null

    # 6.1 复制 UI 主程序及核心文件
    $rootFiles = @(
        "ImgsToPDF.exe",
        "ImgsToPDF.exe.config"
    )
    foreach ($f in $rootFiles) {
        $sourcePath = Join-Path $binDir $f
        if (Test-Path $sourcePath) {
            Copy-Item $sourcePath $stageDir -Force
        } else {
            throw "缺少关键文件: $sourcePath"
        }
    }

    # 6.2 复制语言卫星程序集目录
    $zhDir = Join-Path $binDir "zh"
    if (Test-Path $zhDir) {
        Copy-Item $zhDir $stageDir -Recurse -Force
    }

    # 6.3 复制 Core 目录
    $coreDir = Join-Path $binDir "Core"
    if (Test-Path $coreDir) {
        Copy-Item $coreDir $stageDir -Recurse -Force
    } else {
        throw "缺少 Core 核心模块目录: $coreDir"
    }

    # 6.4 复制开源许可及说明文档
    $docFiles = @("LICENSE.txt", "THIRD-PARTY-NOTICES.md", "README.md")
    foreach ($doc in $docFiles) {
        $docPath = Join-Path $repoRoot $doc
        if (Test-Path $docPath) {
            Copy-Item $docPath $stageDir -Force
        }
    }

    # 确保输出目录存在
    if (-not (Test-Path $OutputDir)) {
        New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
    }

    # 6.5 生成绿色免安装 ZIP 压缩包
    $zipFileName = "ImgsToPDF-v${Version}-win-${Platform}.zip"
    $zipFilePath = Join-Path $OutputDir $zipFileName

    if (Test-Path $zipFilePath) {
        Remove-Item $zipFilePath -Force
    }

    Write-Host "--> 正在生成便携版 ZIP: $zipFilePath" -ForegroundColor Cyan
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        (Join-Path $repoRoot "build\dist"),
        $zipFilePath,
        [System.IO.Compression.CompressionLevel]::Optimal,
        $false
    )

    # 计算 ZIP SHA256 校验和
    $zipHash = (Get-FileHash -Path $zipFilePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $zipHashFile = "$zipFilePath.sha256"
    "$zipHash  $zipFileName" | Set-Content -Path $zipHashFile -Encoding ascii
    Write-Host "  SHA256: $zipHash" -ForegroundColor Gray
    Write-Host "便携版打包完成！" -ForegroundColor Green

    # 6.6 生成 Inno Setup 安装包（当前项目不发布安装包形式的二进制文件，已注释停用）
    if ($Installer) {
        Write-Warning "安装包构建流程已在此版本中注释停用（当前项目仅发布便携版 ZIP 压缩包）。"
    }
    <#
    if ($Installer) {
        Write-Host "`n--> 正在生成 Inno Setup 安装包..." -ForegroundColor Cyan
        
        # 寻找 ISCC.exe
        function Find-ISCC {
            $cmd = Get-Command iscc.exe -ErrorAction SilentlyContinue
            if ($cmd) { return $cmd.Source }
            $paths = @(
                "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
                "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
                "${env:ProgramFiles(x86)}\Inno Setup 5\ISCC.exe",
                "${env:ProgramFiles}\Inno Setup 5\ISCC.exe"
            )
            foreach ($p in $paths) {
                if (Test-Path $p) { return $p }
            }
            return $null
        }

        $isccPath = Find-ISCC
        if (-not $isccPath) {
            Write-Warning "未检测到 Inno Setup (ISCC.exe)。请安装 Inno Setup 6 后重试，或通过 'choco install innosetup' 安装。"
            Write-Warning "已跳过安装包生成，仅生成便携版 ZIP。"
        } else {
            $issFile = Join-Path $repoRoot "build\installer.iss"
            if (-not (Test-Path $issFile)) {
                throw "未找到 Inno Setup 脚本: $issFile"
            }

            $isccArgs = @(
                "/Q",
                "/DMyAppVersion=$Version",
                "/DSourceDir=$stageDir",
                "/DOutputDir=$OutputDir",
                $issFile
            )

            Write-Host "执行: $isccPath $($isccArgs -join ' ')" -ForegroundColor Gray
            $isccProc = Start-Process -FilePath $isccPath -ArgumentList $isccArgs -NoNewWindow -PassThru -Wait
            if ($isccProc.ExitCode -ne 0) {
                throw "Inno Setup 打包失败，退出码: $($isccProc.ExitCode)"
            }

            $installerFileName = "ImgsToPDF-v${Version}-win-${Platform}-Setup.exe"
            $installerFilePath = Join-Path $OutputDir $installerFileName
            if (Test-Path $installerFilePath) {
                $installerHash = (Get-FileHash -Path $installerFilePath -Algorithm SHA256).Hash.ToLowerInvariant()
                $installerHashFile = "$installerFilePath.sha256"
                "$installerHash  $installerFileName" | Set-Content -Path $installerHashFile -Encoding ascii
                Write-Host "  安装包 SHA256: $installerHash" -ForegroundColor Gray
                Write-Host "安装包生成成功！" -ForegroundColor Green
            }
        }
    }
    #>

    Write-Host "`n============================================================" -ForegroundColor Green
    Write-Host "打包产物目录: $OutputDir" -ForegroundColor Green
    Get-ChildItem -Path $OutputDir -File | Select-Object Name, Length, LastWriteTime | Format-Table -AutoSize
    Write-Host "============================================================" -ForegroundColor Green
}
