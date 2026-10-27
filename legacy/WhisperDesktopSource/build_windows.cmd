@echo off
setlocal
where msbuild >nul 2>nul
if errorlevel 1 (
  echo 未找到 MSBuild。
  echo 请用 “x64 Native Tools Command Prompt for VS 2022” 运行此脚本，或安装 Visual Studio 2022 的“使用 C++ 的桌面开发”。
  exit /b 1
)

echo [1/3] 编译 CompressShaders 工具...
msbuild ComputeShaders\ComputeShaders.vcxproj /p:Configuration=Release /p:Platform=x64 /m
if errorlevel 1 exit /b 1

echo [2/3] 打包 GPU 着色器...
dotnet run --project Tools\CompressShaders\CompressShaders.csproj -c Release
if errorlevel 1 exit /b 1

echo [3/3] 编译日语字幕工具...
msbuild WhisperCpp.sln /t:Examples\WhisperDesktop;Whisper /p:Configuration=Release /p:Platform=x64 /m
if errorlevel 1 exit /b 1

echo.
echo 构建完成。请在 x64\Release\ 目录中找到 WhisperDesktop.exe 和 Whisper.dll。
pause
