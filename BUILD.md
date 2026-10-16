# ImgsToPDF 构建与打包指南

本项目提供了一套完整的本地与云端自动化构建、归档及发布管线。

---

## 目录
- [环境要求](#环境要求)
- [本地构建与打包](#本地构建与打包)
  - [快速开始](#快速开始)
  - [脚本参数详解](#脚本参数详解)
  - [常用命令示例](#常用命令示例)
  - [安装包制作 (Inno Setup)](#安装包制作-inno-setup)
- [打包产物目录结构](#打包产物目录结构)
- [CI/CD 云端自动化管线 (GitHub Actions)](#cicd-云端自动化管线-github-actions)
- [常见问题排查](#常见问题排查)

---

## 环境要求

- **操作系统**：Windows 10 / 11 或 Windows Server（64位）
- **目标框架**：.NET Framework 4.8 Developer Pack
- **构建工具**：Visual Studio 2022（17.10+）或 Visual Studio Build Tools，包含以下工作负载或组件：
  - `.NET 桌面开发` (包含 MSBuild 及 C# 编译器)
  - `.NET Framework 4.8 目标包`
- **Shell 环境**：PowerShell 5.1+ 或 PowerShell 7+ (pwsh)
- **可选工具**：[Inno Setup 6](https://jrsoftware.org/isinfo.php)（用于生成 `.exe` 安装程序）

---

## 本地构建与打包

项目根目录下提供了便捷入口 `build.cmd` 以及位于 `build/build.ps1` 的核心 PowerShell 脚本。

### 快速开始

在项目根目录下直接运行：
```cmd
build.cmd
```
或者使用 PowerShell：
```powershell
.\build\build.ps1
```
脚本将自动探测系统中的 MSBuild 工具、还原 NuGet 依赖并编译 `Release | x64` 版本。

---

### 脚本参数详解

| 参数 | 类型 | 默认值 | 说明 |
| :--- | :--- | :--- | :--- |
| `-Configuration` | string | `Release` | 编译配置，可选 `Release` 或 `Debug` |
| `-Platform` | string | `x64` | 目标架构（本项目架构固定为 `x64`） |
| `-Clean` | switch | `false` | 构建前清理以往的 `bin`、`obj` 及发布目录 |
| `-Package` | switch | `false` | 收集发布文件，打包为便携版 ZIP 压缩包并计算 SHA256 |
| `-Installer` | switch | `false` | 调用 Inno Setup 编译器生成 Windows 安装包（如未安装将优雅降级跳过） |
| `-Version` | string | 自动解析 | 自定义版本号（默认自动读取 `AssemblyInfo.cs` 中的版本） |
| `-OutputDir` | string | `artifacts` | 发布产物输出目录 |

---

### 常用命令示例

1. **日常重新编译与清理**：
   ```cmd
   build.cmd -Clean
   ```

2. **完整打包绿色便携版（推荐发布前使用）**：
   ```cmd
   build.cmd -Clean -Package
   ```
   产物将输出至 `artifacts/` 目录：
   - `ImgsToPDF-v<版本>-win-x64.zip`
   - `ImgsToPDF-v<版本>-win-x64.zip.sha256`

3. **打包绿色版 + Windows 安装包**：
   ```cmd
   build.cmd -Clean -Package -Installer
   ```
   额外生成：
   - `ImgsToPDF-v<版本>-win-x64-Setup.exe`
   - `ImgsToPDF-v<版本>-win-x64-Setup.exe.sha256`

4. **指定版本号打包**：
   ```powershell
   .\build\build.ps1 -Package -Version "1.3.1"
   ```

---

### 安装包制作 (Inno Setup)

若需生成 `.exe` 形式的安装程序：
1. 安装 [Inno Setup 6](https://jrsoftware.org/isdl.php)（或通过包管理器 `choco install innosetup` / `winget install JRSoftware.InnoSetup` 安装）。
2. 执行带有 `-Installer` 参数的命令即可。脚本会自动定位 `ISCC.exe` 并完成安装包编译。

安装脚本定义于 [build/installer.iss](/build/installer.iss)，支持桌面图标选择、中文/英文向导界面和卸载功能。

---

## 打包产物目录结构

打包生成的便携版解包后为一个独立的 `ImgsToPDF` 目录：

```text
ImgsToPDF/
├── ImgsToPDF.exe                      # UI 主程序
├── ImgsToPDF.exe.config               # UI 配置文件
├── zh/                                # 多语言卫星程序集
│   └── ImgsToPDF.resources.dll
├── LICENSE.txt                        # 开源许可证
├── THIRD-PARTY-NOTICES.md             # 第三方开源组件声明
├── README.md                          # 说明文档
└── Core/                              # 核心命令行处理模块及依赖
    ├── ImgsToPDFCore.exe              # 核心控制台执行程序
    ├── ImgsToPDFCore.exe.config
    ├── config.lua                     # 运行时 Lua 配置文件
    ├── Modules/                       # Lua 扩展模块 (Common, OSUtil, PathUtil, unicode)
    │   ├── Common.lua
    │   ├── OSUtil.lua
    │   ├── PathUtil.lua
    │   └── unicode.lua
    ├── libwebp_x64.dll                # 原生 WebP 解码库
    ├── xlua.dll                       # 原生 xLua 运行时
    ├── XLua.Mini.dll                  # xLua 托管绑定库
    └── ... (其余 BouncyCastle, itextsharp, SharpCompress 等托管 DLL)
```

---

## CI/CD 云端自动化管线 (GitHub Actions)

仓库已集成两套 GitHub Actions 工作流：

1. **持续集成 (CI)** - [.github/workflows/ci.yml](.github/workflows/ci.yml)：
   - **触发时机**：推送（Push）或提交拉取请求（Pull Request）到 `master` 分支，或在 Actions 页面手动触发。
   - **执行流程**：自动在 Windows 环境中拉取代码、配置 MSBuild、安装 Inno Setup、执行完整打包，并将产物上传为 Actions Artifacts（保留 14 天）。

2. **自动发布 (Release)** - [.github/workflows/release.yml](/.github/workflows/release.yml)：
   - **触发时机**：推送带有 `v*` 前缀的版本标签（例如 `git tag v1.3.1 && git push origin v1.3.1`）。
   - **执行流程**：根据 Git 标签自动提取版本号并完成打包，自动在 GitHub Releases 中创建新版本，并上传对应的 `.zip` 免安装包、`.exe` 安装包以及 `.sha256` 校验和文件，自动生成更新日志。
