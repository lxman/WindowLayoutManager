using System;
using System.IO;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace WindowLayoutManager.Services
{
    /// <summary>
    /// Identity of the active solution: its directory plus the solution file name. Both are needed
    /// to key snapshot storage — microservices repos routinely keep several .sln/.slnf files in one
    /// directory, and keying by directory alone would mix their layouts into a single list.
    /// </summary>
    internal sealed class SolutionInfo
    {
        public SolutionInfo(string directory, string name)
        {
            Directory = directory;
            Name = name;
        }

        /// <summary>Full path of the solution directory.</summary>
        public string Directory { get; }

        /// <summary>Solution file name without extension (directory name in Open Folder mode).</summary>
        public string Name { get; }
    }

    /// <summary>
    /// Tracks the active solution and raises <see cref="SolutionOpened"/> / <see cref="SolutionClosed"/>
    /// so the tool window can swap its snapshot list. Subscribes via <c>IVsSolution.AdviseSolutionEvents</c>
    /// and surfaces the already-open solution at init (the package auto-loads only once one exists).
    /// </summary>
    internal sealed class SolutionContext : IVsSolutionEvents, IDisposable
    {
        private readonly IServiceProvider _serviceProvider;
        private IVsSolution _solution;
        private uint _cookie;

        public SolutionContext(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        /// <summary>Identity of the active solution, or null when none is open.</summary>
        public SolutionInfo CurrentSolution { get; private set; }

        /// <summary>Raised with the solution identity when a solution finishes opening (or is already open at init).</summary>
        public event EventHandler<SolutionInfo> SolutionOpened;

        /// <summary>Raised after the solution closes.</summary>
        public event EventHandler SolutionClosed;

        public void Initialize()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _solution = _serviceProvider.GetService(typeof(SVsSolution)) as IVsSolution;
            if (_solution == null) return;

            _solution.AdviseSolutionEvents(this, out _cookie);

            if (TryGetSolution(out SolutionInfo info))
            {
                CurrentSolution = info;
                SolutionOpened?.Invoke(this, info);
            }
        }

        private bool TryGetSolution(out SolutionInfo info)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            info = null;
            if (_solution == null) return false;
            if (ErrorHandler.Failed(_solution.GetSolutionInfo(out string dir, out string slnFile, out _)))
                return false;
            if (string.IsNullOrEmpty(slnFile) && string.IsNullOrEmpty(dir)) return false;

            string directory = string.IsNullOrEmpty(dir) ? Path.GetDirectoryName(slnFile) : dir;
            if (string.IsNullOrEmpty(directory)) return false;

            // Open Folder mode has no solution file; fall back to the directory name.
            string name = !string.IsNullOrEmpty(slnFile)
                ? Path.GetFileNameWithoutExtension(slnFile)
                : new DirectoryInfo(directory).Name;
            if (string.IsNullOrEmpty(name)) return false;

            info = new SolutionInfo(directory, name);
            return true;
        }

        public int OnAfterOpenSolution(object pUnkReserved, int fNewSolution)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (TryGetSolution(out SolutionInfo info))
            {
                CurrentSolution = info;
                SolutionOpened?.Invoke(this, info);
            }
            return VSConstants.S_OK;
        }

        public int OnAfterCloseSolution(object pUnkReserved)
        {
            CurrentSolution = null;
            SolutionClosed?.Invoke(this, EventArgs.Empty);
            return VSConstants.S_OK;
        }

        public void Dispose()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_cookie != 0 && _solution != null)
            {
                _solution.UnadviseSolutionEvents(_cookie);
                _cookie = 0;
            }
        }

        // Unused IVsSolutionEvents members.
        public int OnAfterOpenProject(IVsHierarchy pHierarchy, int fAdded) => VSConstants.S_OK;
        public int OnQueryCloseProject(IVsHierarchy pHierarchy, int fRemoving, ref int pfCancel) => VSConstants.S_OK;
        public int OnBeforeCloseProject(IVsHierarchy pHierarchy, int fRemoved) => VSConstants.S_OK;
        public int OnAfterLoadProject(IVsHierarchy pStubHierarchy, IVsHierarchy pRealHierarchy) => VSConstants.S_OK;
        public int OnQueryUnloadProject(IVsHierarchy pRealHierarchy, ref int pfCancel) => VSConstants.S_OK;
        public int OnBeforeUnloadProject(IVsHierarchy pRealHierarchy, IVsHierarchy pStubHierarchy) => VSConstants.S_OK;
        public int OnQueryCloseSolution(object pUnkReserved, ref int pfCancel) => VSConstants.S_OK;
        public int OnBeforeCloseSolution(object pUnkReserved) => VSConstants.S_OK;
    }
}
