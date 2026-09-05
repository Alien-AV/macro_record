@echo off
setlocal
set "PUBLISH_DIR=%APPVEYOR_BUILD_FOLDER%\x64\Release\publish"

dotnet publish "%APPVEYOR_BUILD_FOLDER%\MacroRecorderGUI\MacroRecorderGUI.csproj" -c Release -p:Platform=x64 -r win-x64 --self-contained true -o "%PUBLISH_DIR%"
if errorlevel 1 exit /b %errorlevel%

7z a "%APPVEYOR_BUILD_FOLDER%\x64\Release\macro_record-%APPVEYOR_BUILD_VERSION%-x64-Release.zip" "%PUBLISH_DIR%\*"
if errorlevel 1 exit /b %errorlevel%

"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" "%APPVEYOR_BUILD_FOLDER%\inno-setup-script.iss" -DMyAppVersion=%APPVEYOR_BUILD_VERSION% -DArch=x64 -DMyOutputDir="%APPVEYOR_BUILD_FOLDER%\x64\Release" -DMyPublishDir="%PUBLISH_DIR%"
