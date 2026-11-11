@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

set "PROJ_DIR=%~dp0"
set "PROJ_DIR=%PROJ_DIR:~0,-1%"
cd /d "%PROJ_DIR%"

echo ========================================================
echo  開始準備 KanaVox 編譯設定與專案架構...
echo ========================================================

:: 1. 搜尋 MSBuild 引擎路徑
set "MSBUILD_PATH="
if exist "%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe" (
    set "MSBUILD_PATH=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe"
)

if not defined MSBUILD_PATH (
    for /f "usebackq tokens=*" %%i in (`"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe 2^>nul`) do (
        if exist "%%i" set "MSBUILD_PATH=%%i"
    )
)

if not defined MSBUILD_PATH (
    echo [錯誤] 找不到可用的 MSBuild.exe，請確認系統已安裝 .NET Framework 4.8 或 Visual Studio！
    pause
    exit /b 1
)

echo [資訊] 採用建置引擎: "%MSBUILD_PATH%"

:: 2. 確認或產生 KanaVox.csproj
if not exist "%PROJ_DIR%\KanaVox.csproj" (
    (
        echo ^<Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003"^>
        echo   ^<Import Project="$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props" Condition="Exists('$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props')" /^>
        echo   ^<PropertyGroup^>
        echo     ^<Configuration Condition=" '$(Configuration)' == '' "^>Release^</Configuration^>
        echo     ^<Platform Condition=" '$(Platform)' == '' "^>AnyCPU^</Platform^>
        echo     ^<ProjectGuid^>{8C2F3140-AE53-4809-B81E-B23B06927C01}^</ProjectGuid^>
        echo     ^<OutputType^>WinExe^</OutputType^>
        echo     ^<RootNamespace^>KanaVox^</RootNamespace^>
        echo     ^<AssemblyName^>KanaVox^</AssemblyName^>
        echo     ^<TargetFrameworkVersion^>v4.8^</TargetFrameworkVersion^>
        echo     ^<FileAlignment^>512^</FileAlignment^>
        echo     ^<ProjectTypeGuids^>{60dc8134-eba5-43b8-bcc9-bb4bc16c2548};{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}^</ProjectTypeGuids^>
        echo     ^<WarningLevel^>4^</WarningLevel^>
        echo     ^<AutoGenerateBindingRedirects^>true^</AutoGenerateBindingRedirects^>
        echo   ^</PropertyGroup^>
        echo   ^<PropertyGroup Condition=" '$(Configuration)|$(Platform)' == 'Release|AnyCPU' "^>
        echo     ^<PlatformTarget^>AnyCPU^</PlatformTarget^>
        echo     ^<DebugType^>none^</DebugType^>
        echo     ^<Optimize^>true^</Optimize^>
        echo     ^<OutputPath^>bin\Release\^</OutputPath^>
        echo     ^<DefineConstants^>TRACE^</DefineConstants^>
        echo     ^<ErrorReport^>prompt^</ErrorReport^>
        echo     ^<WarningLevel^>4^</WarningLevel^>
        echo     ^<Prefer32Bit^>false^</Prefer32Bit^>
        echo   ^</PropertyGroup^>
        echo   ^<ItemGroup^>
        echo     ^<Reference Include="System" /^>
        echo     ^<Reference Include="System.Core" /^>
        echo     ^<Reference Include="System.Net.Http" /^>
        echo     ^<Reference Include="System.Speech" /^>
        echo     ^<Reference Include="System.Web.Extensions" /^>
        echo     ^<Reference Include="System.Xaml" /^>
        echo     ^<Reference Include="WindowsBase" /^>
        echo     ^<Reference Include="PresentationCore" /^>
        echo     ^<Reference Include="PresentationFramework" /^>
        echo   ^</ItemGroup^>
        echo   ^<ItemGroup^>
        echo     ^<ApplicationDefinition Include="App.xaml"^>
        echo       ^<Generator^>MSBuild:Compile^</Generator^>
        echo       ^<SubType^>Designer^</SubType^>
        echo     ^</ApplicationDefinition^>
        echo     ^<Page Include="MainWindow.xaml"^>
        echo       ^<Generator^>MSBuild:Compile^</Generator^>
        echo       ^<SubType^>Designer^</SubType^>
        echo     ^</Page^>
        echo     ^<Compile Include="App.xaml.cs"^>
        echo       ^<DependentUpon^>App.xaml^</DependentUpon^>
        echo       ^<SubType^>Code^</SubType^>
        echo     ^</Compile^>
        echo     ^<Compile Include="MainWindow.xaml.cs"^>
        echo       ^<DependentUpon^>MainWindow.xaml^</DependentUpon^>
        echo       ^<SubType^>Code^</SubType^>
        echo     ^</Compile^>
        echo     ^<Compile Include="Models.cs" /^>
        echo     ^<Compile Include="AudioService.cs" /^>
        echo     ^<Compile Include="OllamaService.cs" /^>
        echo   ^</ItemGroup^>
        echo   ^<Import Project="$(MSBuildToolsPath)\Microsoft.CSharp.targets" /^>
        echo ^</Project^>
    ) > "%PROJ_DIR%\KanaVox.csproj"
    echo [OK] 已建立 KanaVox.csproj
)

:: 自動將背景常駐代理與相關資料庫腳本同步到 bin\Release\ 供本地直接執行
copy /y "%PROJ_DIR%\edge_tts_proxy.py" "%PROJ_DIR%\bin\Release\" >nul
copy /y "%PROJ_DIR%\generate_clean_kana.py" "%PROJ_DIR%\bin\Release\" >nul
copy /y "%PROJ_DIR%\Start_TtsProxy.bat" "%PROJ_DIR%\bin\Release\" >nul
copy /y "%PROJ_DIR%\mnemonics.json" "%PROJ_DIR%\bin\Release\" >nul
copy /y "%PROJ_DIR%\preset_sentences.json" "%PROJ_DIR%\bin\Release\" >nul

:: 3. 執行 Release 編譯
echo.
echo ========================================================
echo  開始編譯 KanaVox (Release 模式)...
echo ========================================================

"%MSBUILD_PATH%" "%PROJ_DIR%\KanaVox.csproj" /p:Configuration=Release /t:Rebuild /m /nologo
if errorlevel 1 (
    echo.
    echo [失敗] 編譯中斷，請檢視上方錯誤訊息！
    pause
    exit /b 1
)

:: 4. 準備乾淨發布資料夾（過濾個人設定與版權音檔）
echo.
echo ========================================================
echo  開始封裝 GitHub 發布專用 ZIP 壓縮檔...
echo ========================================================

set "STAGING_DIR=%PROJ_DIR%\bin\Release\KanaVox_Staging"
set "OUTPUT_ZIP=%PROJ_DIR%\bin\Release\KanaVox-Release.zip"

if exist "%STAGING_DIR%" rd /s /q "%STAGING_DIR%"
if exist "%OUTPUT_ZIP%" del /f /q "%OUTPUT_ZIP%"

mkdir "%STAGING_DIR%"
mkdir "%STAGING_DIR%\Audio"

:: 複製必要執行檔與相依設定（排除 .pdb 與個人檔案）
copy /y "%PROJ_DIR%\bin\Release\KanaVox.exe" "%STAGING_DIR%\" >nul
if exist "%PROJ_DIR%\bin\Release\KanaVox.exe.config" (
    copy /y "%PROJ_DIR%\bin\Release\KanaVox.exe.config" "%STAGING_DIR%\" >nul
)

:: 複製發布隨附工具與說明文件
if exist "%PROJ_DIR%\generate_audio.py" (
    copy /y "%PROJ_DIR%\generate_audio.py" "%STAGING_DIR%\" >nul
)
if exist "%PROJ_DIR%\README.md" (
    copy /y "%PROJ_DIR%\README.md" "%STAGING_DIR%\" >nul
)
if exist "%PROJ_DIR%\LICENSE" (
    copy /y "%PROJ_DIR%\LICENSE" "%STAGING_DIR%\" >nul
)

:: 在 Audio 目錄放入 .gitkeep 確保空資料夾結構被保留
type nul > "%STAGING_DIR%\Audio\.gitkeep"

:: 5. 呼叫 PowerShell 進行壓縮
powershell -NoProfile -Command "Compress-Archive -Path '%STAGING_DIR%\*' -DestinationPath '%OUTPUT_ZIP%' -Force"

:: 清除暫存目錄
rd /s /q "%STAGING_DIR%"

if exist "%OUTPUT_ZIP%" (
    for %%F in ("%OUTPUT_ZIP%") do set "ZIP_SIZE=%%~zF"
    set /a "ZIP_KB=!ZIP_SIZE! / 1024"
    echo [完成] 已產出純淨發布檔：
    echo        檔案路徑: "%OUTPUT_ZIP%"
    echo        檔案大小: !ZIP_KB! KB
    echo.
    echo [發布檢核確認]
    echo  - [x] KanaVox.exe (已最佳化編譯)
    echo  - [x] generate_audio.py (離線音訊生成腳本)
    echo  - [x] Audio\ (乾淨空資料夾，無版權音檔殘留)
    echo  - [x] 排除 window_settings.json (無個人座標殘留)
    echo  - [x] 排除 custom_sentences.json (無個人自訂例句殘留)
    echo  - [x] 排除 *.pdb (無除錯符號檔案)
) else (
    echo [警告] 壓縮流程未產生 ZIP 檔，請確認 PowerShell 支援度。
)

echo ========================================================
echo  KanaVox 發布流程作業完成！
echo ========================================================
pause