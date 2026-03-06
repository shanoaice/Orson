using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CppSharp;
using CppSharp.AST;
using CppSharp.Generators;
using CppSharp.Parser;
using CppSharp.Passes;

namespace Pacpar.Alpm.BindingsGen;

public class LibAlpm : ILibrary
{
    private static readonly HashSet<string> AlpmHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "alpm.h",
        "alpm_list.h",
    };

    public static int Main(string[] args)
    {
      ConsoleDriver.Run(new LibAlpm());
      return 0;
    }

    public void Setup(Driver driver)
    {
        var options = driver.Options;
        options.GeneratorKind = GeneratorKind.CSharp;
        options.Verbose = true;
        options.Quiet = false;

        var parserOptions = driver.ParserOptions;
        parserOptions.Verbose = true;

        var module = options.AddModule("libalpm");
        module.OutputNamespace = "Pacpar.Alpm.Bindings";

        module.Headers.Add("alpm.h");
        module.Headers.Add("alpm_list.h");
    }

    public void SetupPasses(Driver driver)
    {
    }

    public void Preprocess(Driver driver, ASTContext ctx)
    {
    }

    public void Postprocess(Driver driver, ASTContext ctx)
    {
    }
}
