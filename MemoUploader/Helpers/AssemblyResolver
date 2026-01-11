using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

namespace MemoUploader.Helpers;
{
    public class AssemblyResolver : IDisposable
    {
        private static readonly Regex assemblyNameParser = new Regex(
               @"(?<name>.+?), Version=(?<version>.+?), Culture=(?<culture>.+?), PublicKeyToken=(?<pubkey>.+)",
               RegexOptions.Compiled);

        public List<string> Directories { get; set; }

        public AssemblyResolver(IEnumerable<string> directories)
        {
            this.Directories = new List<string>();
            if (directories != null)
            {
                this.Directories.AddRange(directories);
            }

            AppDomain.CurrentDomain.AssemblyResolve += CustomAssemblyResolve;
        }

        public AssemblyResolver()
               : this(null)
        {
        }

        public void Dispose()
        {
            AppDomain.CurrentDomain.AssemblyResolve -= CustomAssemblyResolve;
        }

        private Assembly CustomAssemblyResolve(object sender, ResolveEventArgs e)
        {
            //Log.D($"尝试寻找程序集: {e.Name}");
            var match = assemblyNameParser.Match(e.Name);

            foreach (var directory in this.Directories)
            {
                var asmPath = "";

                if (match.Success)
                {
                    var asmFileName = match.Groups["name"].Value + ".dll";
                    if (match.Groups["culture"].Value == "neutral")
                    {
                        asmPath = Path.Combine(directory, asmFileName);
                    }
                    else
                    {
                        asmPath = Path.Combine(directory, match.Groups["culture"].Value, asmFileName);
                    }
                }
                else
                {
                    asmPath = Path.Combine(directory, e.Name + ".dll");
                }

                if (File.Exists(asmPath))
                {
                    Assembly asm;
                    asm = Assembly.LoadFile(asmPath);

                    OnAssemblyLoaded(asm);
                    return asm;
                }
            }

            return null;
        }

        protected void OnExceptionOccured(Exception exception)
        {
            if (this.ExceptionOccured != null)
            {
                this.ExceptionOccured(this, new ExceptionOccuredEventArgs(exception));
            }
        }

        protected void OnAssemblyLoaded(Assembly assembly)
        {
            if (this.AssemblyLoaded != null)
            {
                this.AssemblyLoaded(this, new AssemblyLoadEventArgs(assembly));
                //Log.I($"[AssemblyResolver] 已加载程序集: {assembly.FullName}");
            }
        }

        public event EventHandler<ExceptionOccuredEventArgs> ExceptionOccured;

        public event EventHandler<AssemblyLoadEventArgs> AssemblyLoaded;

        public class ExceptionOccuredEventArgs : EventArgs
        {
            public Exception Exception { get; set; }

            public ExceptionOccuredEventArgs(Exception exception)
            {
                this.Exception = exception;
            }
        }
    }
}
