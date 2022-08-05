7z a %APPVEYOR_BUILD_FOLDER%\%PLATFORM%\%CONFIGURATION%\%APPVEYOR_PROJECT_NAME%-%APPVEYOR_BUILD_VERSION%-%PLATFORM%-%CONFIGURATION%.zip %APPVEYOR_BUILD_FOLDER%\%PLATFORM%\%CONFIGURATION%\MacroRecorderGUI.exe

7z a %APPVEYOR_BUILD_FOLDER%\%PLATFORM%\%CONFIGURATION%\%APPVEYOR_PROJECT_NAME%-%APPVEYOR_BUILD_VERSION%-%PLATFORM%-%CONFIGURATION%.zip %APPVEYOR_BUILD_FOLDER%\%PLATFORM%\%CONFIGURATION%\*.dll

IF "%CONFIGURATION%"=="Release" (
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" "%APPVEYOR_BUILD_FOLDER%\inno-setup-script.iss" -DMyAppVersion=%APPVEYOR_BUILD_VERSION% -DArch=%PLATFORM% -DMyOutputDir=%APPVEYOR_BUILD_FOLDER%\%PLATFORM%\%CONFIGURATION%\
)