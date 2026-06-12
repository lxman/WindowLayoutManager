using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using IServiceProvider = System.IServiceProvider;

namespace WindowLayoutManager.Services
{
    /// <summary>
    /// Captures and restores the document-well arrangement (which documents are open and how they are
    /// split/tab-grouped) as an opaque, VS-version-bound blob via <c>IVsUIShellDocumentWindowMgr</c>.
    /// </summary>
    internal sealed class DocumentWellService
    {
        private const uint STREAM_SEEK_SET = 0;

        [DllImport("ole32.dll")]
        private static extern int CreateStreamOnHGlobal(IntPtr hGlobal, [MarshalAs(UnmanagedType.Bool)] bool fDeleteOnRelease, out IStream ppstm);

        /// <summary>Captures the current document-well layout as base64, or null if unavailable.</summary>
        public string CaptureBlob(IServiceProvider serviceProvider)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!(serviceProvider.GetService(typeof(SVsUIShellDocumentWindowMgr)) is IVsUIShellDocumentWindowMgr mgr))
                return null;
            if (CreateStreamOnHGlobal(IntPtr.Zero, true, out IStream stream) != VSConstants.S_OK || stream == null)
                return null;

            if (ErrorHandler.Failed(mgr.SaveDocumentWindowPositions(0, stream)))
                return null;

            byte[] bytes = ReadAll(stream);
            return bytes != null && bytes.Length > 0 ? Convert.ToBase64String(bytes) : null;
        }

        /// <summary>
        /// Reopens documents from a previously captured blob. Synchronous: VS queues the document opens,
        /// but their editor views are realized asynchronously afterward.
        /// </summary>
        public void RestoreBlob(IServiceProvider serviceProvider, string base64)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (string.IsNullOrEmpty(base64)) return;
            if (!(serviceProvider.GetService(typeof(SVsUIShellDocumentWindowMgr)) is IVsUIShellDocumentWindowMgr mgr))
                return;

            byte[] bytes;
            try { bytes = Convert.FromBase64String(base64); }
            catch { return; }

            if (CreateStreamOnHGlobal(IntPtr.Zero, true, out IStream stream) != VSConstants.S_OK || stream == null)
                return;

            WriteAll(stream, bytes);
            Seek(stream, 0);
            mgr.ReopenDocumentWindows(stream);
        }

        private static byte[] ReadAll(IStream stream)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Seek(stream, 0);
            using (var ms = new MemoryStream())
            {
                var buffer = new byte[8192];
                while (true)
                {
                    stream.Read(buffer, (uint)buffer.Length, out uint read);
                    if (read == 0) break;
                    ms.Write(buffer, 0, (int)read);
                }
                return ms.ToArray();
            }
        }

        private static void WriteAll(IStream stream, byte[] bytes)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            stream.Write(bytes, (uint)bytes.Length, out uint _);
        }

        private static void Seek(IStream stream, long position)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            stream.Seek(new LARGE_INTEGER { QuadPart = position }, STREAM_SEEK_SET, null);
        }
    }
}
