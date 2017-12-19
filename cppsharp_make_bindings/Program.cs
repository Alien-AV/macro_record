using CppSharp;
using CppSharp.AST;
using CppSharp.Generators;
using System;

namespace cppsharp_make_bindings
{
    class MacroLibrary:ILibrary
    {
        /// Setup the driver options here.
        void ILibrary.Setup(Driver driver)
        {
            var options = driver.Options;
            options.GeneratorKind = GeneratorKind.CSharp;
            options.OutputDir = "InjectAndCaptureDll";
            var module = options.AddModule("InjectAndCaptureDll");
            module.IncludeDirs.Add("..\\..\\..\\InjectAndCaptureDll");
            module.Headers.Add("InjectAndCaptureDll.h");
            //module.LibraryDirs.Add("..\\..\\..\\InjectAndCaptureDll");
            //module.Libraries.Add("InjectAndCaptureDll.dll");
        }

        /// Setup your passes here.
        void ILibrary.SetupPasses(Driver driver) { }

        /// Do transformations that should happen before passes are processed.
        void ILibrary.Preprocess(Driver driver, ASTContext ctx) { }

        /// Do transformations that should happen after passes are processed.
        void ILibrary.Postprocess(Driver driver, ASTContext ctx) { }
    }

    class Program
    {
        static void Main(string[] args)
        {
            MSVCToolchain.DumpSdks();

            ConsoleDriver.Run(new MacroLibrary());
        }
    }
}
