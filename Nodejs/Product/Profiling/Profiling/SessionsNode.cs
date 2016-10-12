﻿//*********************************************************//
//    Copyright (c) Microsoft. All rights reserved.
//    
//    Apache 2.0 License
//    
//    You may obtain a copy of the License at
//    http://www.apache.org/licenses/LICENSE-2.0
//    
//    Unless required by applicable law or agreed to in writing, software 
//    distributed under the License is distributed on an "AS IS" BASIS, 
//    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or 
//    implied. See the License for the specific language governing 
//    permissions and limitations under the License.
//
//*********************************************************//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.VisualStudioTools.Project;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Shell;
using System.Globalization;

namespace Microsoft.NodejsTools.Profiling {
    /// <summary>
    /// Top level node for our UI Hierarchy which includes all of the various performance sessions.
    /// 
    /// We need one top-level node which we'll add the other nodes to.  We treat the other nodes
    /// as nested hierarchies.
    /// </summary>
    class SessionsNode : BaseHierarchyNode, IVsHierarchyDeleteHandler {
        private readonly List<SessionNode> _sessions = new List<SessionNode>();
        internal readonly EventSinkCollection _sessionsCollection = new EventSinkCollection();
        private readonly IVsUIHierarchyWindow _window;
        internal uint _activeSession = VSConstants.VSITEMID_NIL;
        internal static ImageList _imageList = InitImageList();
        const string _rootName = "Node.js Performance Sessions";

        internal SessionsNode(IVsUIHierarchyWindow window) {
            _window = window;
        }

        public SessionNode ActiveSession {
            get {
                if (_activeSession != VSConstants.VSITEMID_NIL) {
                    return (SessionNode)_sessionsCollection[_activeSession];
                }
                return null;
            }
        }

        internal SessionNode AddTarget(ProfilingTarget target, string filename, bool save) {
            Debug.Assert(filename.EndsWith(NodejsProfilingPackage.PerfFileType, StringComparison.Ordinal));

            // ensure a unique name
            string newBaseName = Path.GetFileNameWithoutExtension(filename);
            string tempBaseName = newBaseName;
            int append = 0;
            bool dupFound;
            do {
                dupFound = false;
                for (int i = 0; i < _sessions.Count; i++) {
                    if (Path.GetFileNameWithoutExtension(_sessions[i].Filename) == newBaseName) {
                        dupFound = true;
                    }
                }
                if (dupFound) {
                    append++;
                    newBaseName = tempBaseName + append;
                }
            } while (dupFound);

            string newFilename = newBaseName + NodejsProfilingPackage.PerfFileType;
            // add directory name back if present...
            string dirName = Path.GetDirectoryName(filename);
            if (!String.IsNullOrEmpty(dirName)) {
                newFilename = Path.Combine(dirName, newFilename);
            }
            filename = newFilename;

            // save to the unique item if desired (we save whenever we have an active solution as we have a place to put it)...
            if (save) {
                using (var fs = new FileStream(filename, FileMode.Create)) {
                    ProfilingTarget.Serializer.Serialize(fs, target);
                }
            }

            var node = OpenTarget(target, filename);

            if (!save) {
                node.MarkDirty();
                node._neverSaved = true;
            }

            return node;
        }

        internal SessionNode OpenTarget(ProfilingTarget target, string filename) {
            for (int i = 0; i < _sessions.Count; i++) {
                if (_sessions[i].Filename == filename) {
                    throw new InvalidOperationException(string.Format(CultureInfo.CurrentCulture, "Performance '{0}' session is already open", filename));
                }
            }

            uint prevSibl;
            if (_sessions.Count > 0) {
                prevSibl = _sessions[_sessions.Count - 1].ItemId;
            } else {
                prevSibl = VSConstants.VSITEMID_NIL;
            }

            var node = new SessionNode(this, target, filename);
            _sessions.Add(node);

            OnItemAdded(VSConstants.VSITEMID_ROOT, prevSibl, node.ItemId);

            if (_activeSession == VSConstants.VSITEMID_NIL) {
                SetActiveSession(node);
            }

            return node;
        }

        internal void SetActiveSession(SessionNode node) {
            uint oldItem = _activeSession;
            if (oldItem != VSConstants.VSITEMID_NIL) {
                _window.ExpandItem(this, _activeSession, EXPANDFLAGS.EXPF_UnBoldItem);
            }

            _activeSession = node.ItemId;

            _window.ExpandItem(this, _activeSession, EXPANDFLAGS.EXPF_BoldItem);
        }

        #region IVsUIHierarchy Members

        public override int GetNestedHierarchy(uint itemid, ref Guid iidHierarchyNested, out IntPtr ppHierarchyNested, out uint pitemidNested) {
            var item = _sessionsCollection[itemid];
            if (item != null) {
                if (iidHierarchyNested == typeof(IVsHierarchy).GUID || iidHierarchyNested == typeof(IVsUIHierarchy).GUID) {
                    ppHierarchyNested = System.Runtime.InteropServices.Marshal.GetComInterfaceForObject(item, typeof(IVsUIHierarchy));
                    pitemidNested = VSConstants.VSITEMID_ROOT;
                    return VSConstants.S_OK;
                }
            }

            return base.GetNestedHierarchy(itemid, ref iidHierarchyNested, out ppHierarchyNested, out pitemidNested);
        }

        public override int GetProperty(uint itemid, int propid, out object pvar) {
            // GetProperty is called many many times for this particular property
            pvar = null;
            var prop = (__VSHPROPID)propid;
            switch (prop) {
                case __VSHPROPID.VSHPROPID_CmdUIGuid:
                    pvar = new Guid(Guids.NodejsProfilingPkgString);
                    break;

                case __VSHPROPID.VSHPROPID_Parent:
                    if (itemid != VSConstants.VSITEMID_ROOT) {
                        pvar = VSConstants.VSITEMID_ROOT;
                    }
                    break;

                case __VSHPROPID.VSHPROPID_FirstChild:
                    if (itemid == VSConstants.VSITEMID_ROOT && _sessions.Count > 0)
                        pvar = _sessions[0].ItemId;
                    else
                        pvar = VSConstants.VSITEMID_NIL;
                    break;

                case __VSHPROPID.VSHPROPID_NextSibling:
                    pvar = VSConstants.VSITEMID_NIL;
                    for(int i = 0; i<_sessions.Count; i++) {
                        if (_sessions[i].ItemId == itemid && i < _sessions.Count - 1) {
                            pvar = _sessions[i + 1].ItemId;
                        }
                    }
                    break;

                case __VSHPROPID.VSHPROPID_Expandable:
                    pvar = true;
                    break;

                case __VSHPROPID.VSHPROPID_ExpandByDefault:
                    if (itemid == VSConstants.VSITEMID_ROOT) {
                        pvar = true;
                    }
                    break;

                case __VSHPROPID.VSHPROPID_IconImgList:
                case __VSHPROPID.VSHPROPID_OpenFolderIconHandle:
                    pvar = (int)_imageList.Handle;
                    break;

                case __VSHPROPID.VSHPROPID_IconIndex:
                case __VSHPROPID.VSHPROPID_OpenFolderIconIndex:
                    if (itemid == VSConstants.VSITEMID_ROOT) {
                        pvar = 0;
                    } else {
                        pvar = (int)TreeViewIconIndex.PerformanceSession;
                    }
                    break;
                case __VSHPROPID.VSHPROPID_SaveName:
                    if (itemid != VSConstants.VSITEMID_ROOT) {
                        pvar = GetItem(itemid).Filename;
                    }
                    break;
                case __VSHPROPID.VSHPROPID_Caption:
                    if (itemid != VSConstants.VSITEMID_ROOT) {
                        pvar = GetItem(itemid).Caption;
                    }
                    break;

                case __VSHPROPID.VSHPROPID_ParentHierarchy:
                    if (_sessionsCollection[itemid] != null) {
                        pvar = this as IVsHierarchy;
                    }
                    break;
            }

            if (pvar != null)
                return VSConstants.S_OK;

            return VSConstants.DISP_E_MEMBERNOTFOUND;
        }

        #endregion

        #region IVsHierarchyDeleteHandler Members

        public int DeleteItem(uint dwDelItemOp, uint itemid) {
            var item = GetItem(itemid);
            if (item != null) {
                switch ((__VSDELETEITEMOPERATION)dwDelItemOp) {
                    case __VSDELETEITEMOPERATION.DELITEMOP_DeleteFromStorage:
                        File.Delete(item.Filename);
                        goto case __VSDELETEITEMOPERATION.DELITEMOP_RemoveFromProject;
                    case __VSDELETEITEMOPERATION.DELITEMOP_RemoveFromProject:
                        item.Removed();
                        _sessions.Remove(item);
                        _sessionsCollection.RemoveAt(itemid);
                        OnItemDeleted(itemid);
                        // our itemids have all changed, invalidate them
                        OnInvalidateItems(VSConstants.VSITEMID_ROOT);
                        break;
                }

                if (itemid == _activeSession) {
                    if (_sessions.Count > 0) {
                        SetActiveSession(_sessions[0]);
                    } else {
                        _activeSession = VSConstants.VSITEMID_NIL;
                    }
                }
                return VSConstants.S_OK;
            }
            return VSConstants.E_FAIL;
        }

        public int QueryDeleteItem(uint dwDelItemOp, uint itemid, out int pfCanDelete) {
            pfCanDelete = 1;
            return VSConstants.S_OK;
        }

        #endregion

        private static ImageList InitImageList() {
            var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Microsoft.NodejsTools.Profiling.ProfilingTreeView.bmp");
            return Utilities.GetImageList(
                stream
            );
        }

        private SessionNode GetItem(uint itemid) {
            return (SessionNode)_sessionsCollection[itemid];
        }


        internal void StartProfiling() {
            if (_activeSession != VSConstants.VSITEMID_NIL) {
                GetItem(_activeSession).StartProfiling();
            }
        }

        public List<SessionNode> Sessions {
            get {
                return _sessions;
            }
        }
    }
}
