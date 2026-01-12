using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;


namespace MemoUploader.Helpers;

internal class AssemblyResolver : IDisposable
{
    private static readonly Regex AssemblyNameParser = new(
        @"(?<name>.+?), Version=(?<version>.+?), Culture=(?<culture>.+?), PublicKeyToken=(?<pubkey>.+)",
        RegexOptions.Compiled);

    private List<string> Directories { get; }

    public AssemblyResolver(IEnumerable<string>? directories)
    {
        Directories = [];
        if (directories != null)
            Directories.AddRange(directories);

        AppDomain.CurrentDomain.AssemblyResolve += CustomAssemblyResolve;
    }

    public AssemblyResolver()
        : this(null) { }

    public void Dispose()
        => AppDomain.CurrentDomain.AssemblyResolve -= CustomAssemblyResolve;

    private Assembly? CustomAssemblyResolve(object sender, ResolveEventArgs e)
    {
        var match = AssemblyNameParser.Match(e.Name);

        foreach (var directory in Directories)
        {
            string asmPath;

            if (match.Success)
            {
                var asmFileName = match.Groups["name"].Value + ".dll";
                asmPath = match.Groups["culture"].Value == "neutral" ? Path.Combine(directory, asmFileName) : Path.Combine(directory, match.Groups["culture"].Value, asmFileName);
            }
            else
                asmPath = Path.Combine(directory, e.Name + ".dll");

            if (!File.Exists(asmPath))
                continue;

            var asm = Assembly.LoadFile(asmPath);

            RaiseAssemblyLoaded(asm);
            return asm;
        }

        return null;
    }

    protected void RaiseExceptionOccured(Exception exception)
        => OnExceptionOccured?.Invoke(this, new ExceptionOccuredEventArgs(exception));

    private void RaiseAssemblyLoaded(Assembly assembly)
        => OnAssemblyLoaded?.Invoke(this, new AssemblyLoadEventArgs(assembly));

    public event EventHandler<ExceptionOccuredEventArgs>? OnExceptionOccured;

    public event EventHandler<AssemblyLoadEventArgs>? OnAssemblyLoaded;

    public class ExceptionOccuredEventArgs(Exception exception) : EventArgs
    {
        internal Exception Exception { get; set; } = exception;
    }
}
