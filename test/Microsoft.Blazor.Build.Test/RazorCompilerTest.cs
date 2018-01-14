﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Blazor.Build.Core.RazorCompilation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Xunit;

namespace Microsoft.Blazor.Build.Test
{
    public class RazorCompilerTest
    {
        [Fact]
        public void RejectsInvalidClassName()
        {
            // Arrange/Act
            var result = CompileToCSharp(
                "x:\\dir\\subdir",
                "Filename with spaces.cshtml",
                "ignored code",
                "ignored namespace");

            // Assert
            Assert.Collection(result.Diagnostics,
                item =>
                {
                    Assert.Equal(RazorCompilerDiagnostic.DiagnosticType.Error, item.Type);
                    Assert.StartsWith($"Invalid name 'Filename with spaces'", item.Message);
                });
        }

        [Theory]
        [InlineData("\\unrelated.cs")]
        [InlineData("..\\outsideroot.cs")]
        public void RejectsFilenameOutsideRoot(string filename)
        {
            // Arrange/Act
            var result = CompileToCSharp(
                "x:\\dir\\subdir",
                filename,
                "ignored code",
                "ignored namespace");

            // Assert
            Assert.Collection(result.Diagnostics,
                item =>
                {
                    Assert.Equal(RazorCompilerDiagnostic.DiagnosticType.Error, item.Type);
                    Assert.StartsWith($"File is not within source root directory: '{filename}'", item.Message);
                });
        }

        [Theory]
        [InlineData("ItemAtRoot.cs", "Test.Base", "ItemAtRoot")]
        [InlineData(".\\ItemAtRoot.cs", "Test.Base", "ItemAtRoot")]
        [InlineData("x:\\dir\\subdir\\ItemAtRoot.cs", "Test.Base", "ItemAtRoot")]
        [InlineData("Dir1\\MyFile.cs", "Test.Base.Dir1", "MyFile")]
        [InlineData("Dir1\\Dir2\\MyFile.cs", "Test.Base.Dir1.Dir2", "MyFile")]
        public void CreatesClassWithCorrectNameAndNamespace(string relativePath, string expectedNamespace, string expectedClassName)
        {
            // Arrange/Acts
            var result = CompileToAssembly(
                "x:\\dir\\subdir",
                relativePath,
                "{* No code *}",
                "Test.Base");

            // Assert
            Assert.Empty(result.Diagnostics);
            Assert.Collection(result.Assembly.GetTypes(),
                type =>
                {
                    Assert.Equal(expectedNamespace, type.Namespace);
                    Assert.Equal(expectedClassName, type.Name);
                });
        }

        private static CompileToAssemblyResult CompileToAssembly(string cshtmlRootPath, string cshtmlRelativePath, string cshtmlContent, string outputNamespace)
        {
            var csharpResult = CompileToCSharp(cshtmlRootPath, cshtmlRelativePath, cshtmlContent, outputNamespace);
            if (csharpResult.Diagnostics.Any())
            {
                var diagnosticsLog = string.Join(Environment.NewLine,
                    csharpResult.Diagnostics.Select(d => d.FormatForConsole()).ToArray());
                throw new InvalidOperationException($"Aborting compilation to assembly because RazorCompiler returned nonempty diagnostics: {diagnosticsLog}");
            }

            var syntaxTrees = new[]
            {
                CSharpSyntaxTree.ParseText(csharpResult.Code)
            };
            var references = new[]
            {
                MetadataReference.CreateFromFile(typeof(object).GetTypeInfo().Assembly.Location)
            };
            var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);
            var assemblyName = "TestAssembly" + Guid.NewGuid().ToString("N");
            var compilation = CSharpCompilation.Create(assemblyName,
                syntaxTrees,
                references,
                options);

            using (var peStream = new MemoryStream())
            {
                compilation.Emit(peStream);

                var diagnostics = compilation.GetDiagnostics();
                return new CompileToAssemblyResult
                {
                    Diagnostics = diagnostics,
                    VerboseLog = csharpResult.VerboseLog,
                    Assembly = diagnostics.Any() ? null : Assembly.Load(peStream.ToArray())
                };
            }
        }

        private static CompileToCSharpResult CompileToCSharp(string cshtmlRootPath, string cshtmlRelativePath, string cshtmlContent, string outputNamespace)
        {
            using (var resultStream = new MemoryStream())
            using (var resultWriter = new StreamWriter(resultStream))
            using (var verboseLogStream = new MemoryStream())
            using (var verboseWriter = new StreamWriter(verboseLogStream))
            using (var inputReader = new StringReader(cshtmlContent))
            {
                var diagnostics = new RazorCompiler().CompileSingleFile(
                    cshtmlRootPath,
                    cshtmlRelativePath,
                    inputReader,
                    outputNamespace,
                    resultWriter,
                    verboseWriter);

                resultWriter.Flush();
                verboseWriter.Flush();
                return new CompileToCSharpResult
                {
                    Code = Encoding.UTF8.GetString(resultStream.ToArray()),
                    VerboseLog = Encoding.UTF8.GetString(verboseLogStream.ToArray()),
                    Diagnostics = diagnostics
                };
            }
        }

        private class CompileToCSharpResult
        {
            public string Code { get; set; }
            public string VerboseLog { get; set; }
            public IEnumerable<RazorCompilerDiagnostic> Diagnostics { get; set; }
        }

        private class CompileToAssemblyResult
        {
            public Assembly Assembly { get; set; }
            public string VerboseLog { get; set; }
            public IEnumerable<Diagnostic> Diagnostics { get; set; }
        }
    }
}