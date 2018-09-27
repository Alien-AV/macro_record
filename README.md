# InjectAndCaptureDll
the DLL is the C++ code which does the injecting and capturing of input
it exposes an API which is used by the UI part, they communicate using protobuf
# InjectAndCaptureDllTest
unittests for the dll
# MacroRecorderGUI
C# WPF project, calls InjectAndCaptureDll for the actual work
# macro_record
c++ console executable that's using the dll
