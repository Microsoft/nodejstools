/* ****************************************************************************
 *
 * Copyright (c) Microsoft Corporation. 
 *
 * This source code is subject to terms and conditions of the Apache License, Version 2.0. A 
 * copy of the license can be found in the License.html file at the root of this distribution. If 
 * you cannot locate the Apache License, Version 2.0, please send an email to 
 * vspython@microsoft.com. By using this source code in any fashion, you are agreeing to be bound 
 * by the terms of the Apache License, Version 2.0.
 *
 * You must not remove this notice, or any other, from this software.
 *
 * ***************************************************************************/

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Security.Permissions;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;

namespace Microsoft.VisualStudioTools.Project {
    internal enum tagDVASPECT {
        DVASPECT_CONTENT = 1,
        DVASPECT_THUMBNAIL = 2,
        DVASPECT_ICON = 4,
        DVASPECT_DOCPRINT = 8
    }

    internal enum tagTYMED {
        TYMED_HGLOBAL = 1,
        TYMED_FILE = 2,
        TYMED_ISTREAM = 4,
        TYMED_ISTORAGE = 8,
        TYMED_GDI = 16,
        TYMED_MFPICT = 32,
        TYMED_ENHMF = 64,
        TYMED_NULL = 0
    }

    internal sealed class DataCacheEntry : IDisposable {
        #region fields
        /// <summary>
        /// Defines an object that will be a mutex for this object for synchronizing thread calls.
        /// </summary>
        private static volatile object Mutex = new object();

        private FORMATETC format;

        private long data;

        private DATADIR dataDir;

        private bool isDisposed;
        #endregion

        #region properties
        internal FORMATETC Format {
            get {
                return this.format;
            }
        }

        internal long Data {
            get {
                return this.data;
            }
        }

        internal DATADIR DataDir {
            get {
                return this.dataDir;
            }
        }

        #endregion

        /// <summary>
        /// The IntPtr is data allocated that should be removed. It is allocated by the ProcessSelectionData method.
        /// </summary>
        internal DataCacheEntry(FORMATETC fmt, IntPtr data, DATADIR dir) {
            this.format = fmt;
            this.data = (long)data;
            this.dataDir = dir;
        }

        #region Dispose
        ~DataCacheEntry() {
            Dispose(false);
        }

        /// <summary>
        /// The IDispose interface Dispose method for disposing the object determinastically.
        /// </summary>
        public void Dispose() {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// The method that does the cleanup.
        /// </summary>
        /// <param name="disposing"></param>
        private void Dispose(bool disposing) {
            // Everybody can go here.
            if (!this.isDisposed) {
                // Synchronize calls to the Dispose simulteniously.
                lock (Mutex) {
                    if (disposing && this.data != 0) {
                        Marshal.FreeHGlobal((IntPtr)this.data);
                        this.data = 0;
                    }

                    this.isDisposed = true;
                }
            }
        }
        #endregion
    }

    /// <summary>
    /// Unfortunately System.Windows.Forms.IDataObject and
    /// Microsoft.VisualStudio.OLE.Interop.IDataObject are different...
    /// </summary>
    internal sealed class DataObject : IDataObject {
        #region fields
        internal const int DATA_S_SAMEFORMATETC = 0x00040130;
        EventSinkCollection map;
        ArrayList entries;
        #endregion

        internal DataObject() {
            this.map = new EventSinkCollection();
            this.entries = new ArrayList();
        }

        internal void SetData(FORMATETC format, IntPtr data) {
            this.entries.Add(new DataCacheEntry(format, data, DATADIR.DATADIR_SET));
        }

        #region IDataObject methods
        int IDataObject.DAdvise(FORMATETC[] e, uint adv, IAdviseSink sink, out uint cookie) {
            Utilities.ArgumentNotNull("e", e);

            STATDATA sdata = new STATDATA();

            sdata.ADVF = adv;
            sdata.FORMATETC = e[0];
            sdata.pAdvSink = sink;
            cookie = this.map.Add(sdata);
            sdata.dwConnection = cookie;
            return 0;
        }

        void IDataObject.DUnadvise(uint cookie) {
            this.map.RemoveAt(cookie);
        }

        int IDataObject.EnumDAdvise(out IEnumSTATDATA e) {
            e = new EnumSTATDATA((IEnumerable)this.map);
            return 0; //??
        }

        int IDataObject.EnumFormatEtc(uint direction, out IEnumFORMATETC penum) {
            penum = new EnumFORMATETC((DATADIR)direction, (IEnumerable)this.entries);
            return 0;
        }

        int IDataObject.GetCanonicalFormatEtc(FORMATETC[] format, FORMATETC[] fmt) {
            throw new System.Runtime.InteropServices.COMException("", DATA_S_SAMEFORMATETC);
        }

        void IDataObject.GetData(FORMATETC[] fmt, STGMEDIUM[] m) {
            STGMEDIUM retMedium = new STGMEDIUM();

            if (fmt == null || fmt.Length < 1)
                return;

            foreach (DataCacheEntry e in this.entries) {
                if (e.Format.cfFormat == fmt[0].cfFormat /*|| fmt[0].cfFormat == InternalNativeMethods.CF_HDROP*/) {
                    retMedium.tymed = e.Format.tymed;

                    // Caller must delete the memory.
                    retMedium.unionmember = DragDropHelper.CopyHGlobal(new IntPtr(e.Data));
                    break;
                }
            }

            if (m != null && m.Length > 0)
                m[0] = retMedium;
        }

        void IDataObject.GetDataHere(FORMATETC[] fmt, STGMEDIUM[] m) {
        }

        int IDataObject.QueryGetData(FORMATETC[] fmt) {
            if (fmt == null || fmt.Length < 1)
                return VSConstants.S_FALSE;

            foreach (DataCacheEntry e in this.entries) {
                if (e.Format.cfFormat == fmt[0].cfFormat /*|| fmt[0].cfFormat == InternalNativeMethods.CF_HDROP*/)
                    return VSConstants.S_OK;
            }

            return VSConstants.S_FALSE;
        }

        void IDataObject.SetData(FORMATETC[] fmt, STGMEDIUM[] m, int fRelease) {
        }
        #endregion
    }

    [SecurityPermissionAttribute(SecurityAction.Demand, Flags = SecurityPermissionFlag.UnmanagedCode)]
    internal static class DragDropHelper {
#pragma warning disable 414
        internal static readonly ushort CF_VSREFPROJECTITEMS;
        internal static readonly ushort CF_VSSTGPROJECTITEMS;
        internal static readonly ushort CF_VSPROJECTCLIPDESCRIPTOR;
#pragma warning restore 414

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1810:InitializeReferenceTypeStaticFieldsInline")]
        static DragDropHelper() {
            CF_VSREFPROJECTITEMS = (ushort)UnsafeNativeMethods.RegisterClipboardFormat("CF_VSREFPROJECTITEMS");
            CF_VSSTGPROJECTITEMS = (ushort)UnsafeNativeMethods.RegisterClipboardFormat("CF_VSSTGPROJECTITEMS");
            CF_VSPROJECTCLIPDESCRIPTOR = (ushort)UnsafeNativeMethods.RegisterClipboardFormat("CF_PROJECTCLIPBOARDDESCRIPTOR");
        }

        public static FORMATETC CreateFormatEtc(ushort iFormat) {
            FORMATETC fmt = new FORMATETC();
            fmt.cfFormat = iFormat;
            fmt.ptd = IntPtr.Zero;
            fmt.dwAspect = (uint)DVASPECT.DVASPECT_CONTENT;
            fmt.lindex = -1;
            fmt.tymed = (uint)TYMED.TYMED_HGLOBAL;
            return fmt;
        }

        public static int QueryGetData(Microsoft.VisualStudio.OLE.Interop.IDataObject pDataObject, ref FORMATETC fmtetc) {
            FORMATETC[] af = new FORMATETC[1];
            af[0] = fmtetc;
            int result = pDataObject.QueryGetData(af);
            if (result == VSConstants.S_OK) {
                fmtetc = af[0];
                return VSConstants.S_OK;
            }
            return result;
        }

        public static STGMEDIUM GetData(Microsoft.VisualStudio.OLE.Interop.IDataObject pDataObject, ref FORMATETC fmtetc) {
            FORMATETC[] af = new FORMATETC[1];
            af[0] = fmtetc;
            STGMEDIUM[] sm = new STGMEDIUM[1];
            pDataObject.GetData(af, sm);
            fmtetc = af[0];
            return sm[0];
        }

        /// <summary>
        /// Retrieves data from a VS format.
        /// </summary>
        public static List<string> GetDroppedFiles(ushort format, Microsoft.VisualStudio.OLE.Interop.IDataObject dataObject, out DropDataType ddt) {
            ddt = DropDataType.None;
            List<string> droppedFiles = new List<string>();

            // try HDROP
            FORMATETC fmtetc = CreateFormatEtc(format);

            if (QueryGetData(dataObject, ref fmtetc) == VSConstants.S_OK) {
                STGMEDIUM stgmedium = DragDropHelper.GetData(dataObject, ref fmtetc);
                if (stgmedium.tymed == (uint)TYMED.TYMED_HGLOBAL) {
                    // We are releasing the cloned hglobal here.
                    IntPtr dropInfoHandle = stgmedium.unionmember;
                    if (dropInfoHandle != IntPtr.Zero) {
                        ddt = DropDataType.Shell;
                        try {
                            uint numFiles = UnsafeNativeMethods.DragQueryFile(dropInfoHandle, 0xFFFFFFFF, null, 0);

                            // We are a directory based project thus a projref string is placed on the clipboard.
                            // We assign the maximum length of a projref string.
                            // The format of a projref is : <Proj Guid>|<project rel path>|<file path>
                            uint lenght = (uint)Guid.Empty.ToString().Length + 2 * NativeMethods.MAX_PATH + 2;
                            char[] moniker = new char[lenght + 1];
                            for (uint fileIndex = 0; fileIndex < numFiles; fileIndex++) {
                                uint queryFileLength = UnsafeNativeMethods.DragQueryFile(dropInfoHandle, fileIndex, moniker, lenght);
                                string filename = new String(moniker, 0, (int)queryFileLength);
                                droppedFiles.Add(filename);
                            }
                        } finally {
                            Marshal.FreeHGlobal(dropInfoHandle);
                        }
                    }
                }
            }

            return droppedFiles;
        }

        public static string GetSourceProjectPath(Microsoft.VisualStudio.OLE.Interop.IDataObject dataObject) {
            string projectPath = null;
            FORMATETC fmtetc = CreateFormatEtc(CF_VSPROJECTCLIPDESCRIPTOR);

            if (QueryGetData(dataObject, ref fmtetc) == VSConstants.S_OK) {
                STGMEDIUM stgmedium = DragDropHelper.GetData(dataObject, ref fmtetc);
                if (stgmedium.tymed == (uint)TYMED.TYMED_HGLOBAL) {
                    // We are releasing the cloned hglobal here.
                    IntPtr dropInfoHandle = stgmedium.unionmember;
                    if (dropInfoHandle != IntPtr.Zero) {
                        try {
                            string path = GetData(dropInfoHandle);

                            // Clone the path that we can release our memory.
                            if (!String.IsNullOrEmpty(path)) {
                                projectPath = String.Copy(path);
                            }
                        } finally {
                            Marshal.FreeHGlobal(dropInfoHandle);
                        }
                    }
                }
            }

            return projectPath;
        }

        /// <summary>
        /// Returns the data packed after the DROPFILES structure.
        /// </summary>
        /// <param name="dropHandle"></param>
        /// <returns></returns>
        internal static string GetData(IntPtr dropHandle) {
            IntPtr data = UnsafeNativeMethods.GlobalLock(dropHandle);
            try {
                _DROPFILES df = (_DROPFILES)Marshal.PtrToStructure(data, typeof(_DROPFILES));
                if (df.fWide != 0) {
                    IntPtr pdata = new IntPtr((long)data + df.pFiles);
                    return Marshal.PtrToStringUni(pdata);
                }
            } finally {
                if (data != IntPtr.Zero) {
                    UnsafeNativeMethods.GlobalUnLock(data);
                }
            }

            return null;
        }

        internal static IntPtr CopyHGlobal(IntPtr data) {
            IntPtr src = UnsafeNativeMethods.GlobalLock(data);
            var size = UnsafeNativeMethods.GlobalSize(data).ToInt32();
            IntPtr ptr = Marshal.AllocHGlobal(size);
            IntPtr buffer = UnsafeNativeMethods.GlobalLock(ptr);

            try {
                for (int i = 0; i < size; i++) {
                    byte val = Marshal.ReadByte(new IntPtr((long)src + i));

                    Marshal.WriteByte(new IntPtr((long)buffer + i), val);
                }
            } finally {
                if (buffer != IntPtr.Zero) {
                    UnsafeNativeMethods.GlobalUnLock(buffer);
                }

                if (src != IntPtr.Zero) {
                    UnsafeNativeMethods.GlobalUnLock(src);
                }
            }
            return ptr;
        }

        internal static void CopyStringToHGlobal(string s, IntPtr data, int bufferSize) {
            Int16 nullTerminator = 0;
            int dwSize = Marshal.SizeOf(nullTerminator);

            if ((s.Length + 1) * Marshal.SizeOf(s[0]) > bufferSize)
                throw new System.IO.InternalBufferOverflowException();
            // IntPtr memory already locked...
            for (int i = 0, len = s.Length; i < len; i++) {
                Marshal.WriteInt16(data, i * dwSize, s[i]);
            }
            // NULL terminate it
            Marshal.WriteInt16(new IntPtr((long)data + (s.Length * dwSize)), nullTerminator);
        }
    } // end of dragdrophelper

    internal class EnumSTATDATA : IEnumSTATDATA {
        IEnumerable i;

        IEnumerator e;

        public EnumSTATDATA(IEnumerable i) {
            this.i = i;
            this.e = i.GetEnumerator();
        }

        void IEnumSTATDATA.Clone(out IEnumSTATDATA clone) {
            clone = new EnumSTATDATA(i);
        }

        int IEnumSTATDATA.Next(uint celt, STATDATA[] d, out uint fetched) {
            uint rc = 0;
            //uint size = (fetched != null) ? fetched[0] : 0;
            for (uint i = 0; i < celt; i++) {
                if (e.MoveNext()) {
                    STATDATA sdata = (STATDATA)e.Current;

                    rc++;
                    if (d != null && d.Length > i) {
                        d[i] = sdata;
                    }
                }
            }

            fetched = rc;
            return 0;
        }

        int IEnumSTATDATA.Reset() {
            e.Reset();
            return 0;
        }

        int IEnumSTATDATA.Skip(uint celt) {
            for (uint i = 0; i < celt; i++) {
                e.MoveNext();
            }

            return 0;
        }
    }

    internal class EnumFORMATETC : IEnumFORMATETC {
        IEnumerable cache; // of DataCacheEntrys.

        DATADIR dir;

        IEnumerator e;

        public EnumFORMATETC(DATADIR dir, IEnumerable cache) {
            this.cache = cache;
            this.dir = dir;
            e = cache.GetEnumerator();
        }

        void IEnumFORMATETC.Clone(out IEnumFORMATETC clone) {
            clone = new EnumFORMATETC(dir, cache);
        }

        int IEnumFORMATETC.Next(uint celt, FORMATETC[] d, uint[] fetched) {
            uint rc = 0;
            //uint size = (fetched != null) ? fetched[0] : 0;
            for (uint i = 0; i < celt; i++) {
                if (e.MoveNext()) {
                    DataCacheEntry entry = (DataCacheEntry)e.Current;

                    rc++;
                    if (d != null && d.Length > i) {
                        d[i] = entry.Format;
                    }
                } else {
                    return VSConstants.S_FALSE;
                }
            }

            if (fetched != null && fetched.Length > 0)
                fetched[0] = rc;
            return VSConstants.S_OK;
        }

        int IEnumFORMATETC.Reset() {
            e.Reset();
            return 0;
        }

        int IEnumFORMATETC.Skip(uint celt) {
            for (uint i = 0; i < celt; i++) {
                e.MoveNext();
            }

            return 0;
        }
    }
}
