﻿using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Utilities;
using System;
using System.ComponentModel.Composition;
using System.Linq;
using System.Runtime.InteropServices;

namespace TailwindCSSIntellisense.Completions.Controllers
{
    #region Command Filter

    [Export(typeof(IVsTextViewCreationListener))]
    [ContentType("html")]
    [ContentType("WebForms")]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    internal sealed class HtmlCompletionController : IVsTextViewCreationListener
    {
        [Import]
        internal IVsEditorAdaptersFactoryService AdaptersFactory { get; set; }

        [Import]
        internal ICompletionBroker CompletionBroker { get; set; }

        public void VsTextViewCreated(IVsTextView textViewAdapter)
        {
            IWpfTextView view = AdaptersFactory.GetWpfTextView(textViewAdapter);

            var filter = new HtmlCommandFilter(view, CompletionBroker);

            textViewAdapter.AddCommandFilter(filter, out var next);
            filter.Next = next;
        }
    }

    internal sealed class HtmlCommandFilter : IOleCommandTarget
    {
        ICompletionSession _currentSession;

        public HtmlCommandFilter(IWpfTextView textView, ICompletionBroker broker)
        {
            _currentSession = null;

            TextView = textView;
            Broker = broker;
        }

        public IWpfTextView TextView { get; private set; }
        public ICompletionBroker Broker { get; private set; }
        public IOleCommandTarget Next { get; set; }

        private char GetTypeChar(IntPtr pvaIn)
        {
            return (char)(ushort)Marshal.GetObjectForNativeVariant(pvaIn);
        }

        public int Exec(ref Guid pguidCmdGroup, uint nCmdID, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // Is the caret in a class="" scope?
            if (IsInClassScope(out string classText) == false)
            {
                return Next.Exec(pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);
            }

            bool handled = false;
            bool retrigger = false;
            int hresult = VSConstants.S_OK;

            // 1. Pre-process
            if (pguidCmdGroup == VSConstants.VSStd2K)
            {
                switch ((VSConstants.VSStd2KCmdID)nCmdID)
                {
                    case VSConstants.VSStd2KCmdID.AUTOCOMPLETE:
                    case VSConstants.VSStd2KCmdID.COMPLETEWORD:
                        handled = StartSession();
                        Filter();
                        break;
                    case VSConstants.VSStd2KCmdID.RETURN:
                        handled = Complete(false);
                        break;
                    case VSConstants.VSStd2KCmdID.TAB:
                        handled = Complete(true);
                        retrigger = RetriggerIntellisense(classText);
                        break;
                    case VSConstants.VSStd2KCmdID.CANCEL:
                        handled = Cancel();
                        break;
                }
            }
            else if (pguidCmdGroup == VSConstants.GUID_VSStandardCommandSet97)
            {
                switch ((VSConstants.VSStd97CmdID)nCmdID)
                {
                    case VSConstants.VSStd97CmdID.Paste:
                    case VSConstants.VSStd97CmdID.Undo:
                        Cancel();
                        break;
                }
            }

            if (!handled)
            {
                hresult = Next.Exec(pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);
            }

            if (retrigger)
            {
                StartSession();
            }

            if (ErrorHandler.Succeeded(hresult))
            {
                if (pguidCmdGroup == VSConstants.VSStd2K)
                {
                    switch ((VSConstants.VSStd2KCmdID)nCmdID)
                    {
                        case VSConstants.VSStd2KCmdID.TYPECHAR:
                            var character = GetTypeChar(pvaIn);
                            if (_currentSession == null || CharsAfterSignificantPoint(classText) <= 1 || character == ':' || character == '/')
                            {
                                _currentSession?.Dismiss();
                                StartSession();
                                Filter();
                            }
                            else if (_currentSession != null)
                            {
                                DismissOtherSessions();
                                Filter();
                            }
                            break;
                        case VSConstants.VSStd2KCmdID.DELETEWORDLEFT:
                            _currentSession?.Dismiss();
                            StartSession();
                            break;
                        case VSConstants.VSStd2KCmdID.BACKSPACE:
                            if (classText.Any() && char.IsWhiteSpace(classText.Last()))
                            {
                                break;
                            }
                            if (_currentSession == null || CharsAfterSignificantPoint(classText) <= 2 || classText.EndsWith("/"))
                            {
                                _currentSession?.Dismiss();
                                StartSession();
                                Filter();
                            }
                            else if (_currentSession != null)
                            {
                                DismissOtherSessions();
                                Filter();
                            }
                            break;
                    }
                }
            }

            return hresult;
        }

        private bool IsInClassScope(out string classText)
        {
            var startPos = new SnapshotPoint(TextView.TextSnapshot, 0);
            var caretPos = TextView.Caret.Position.BufferPosition;

            var searchSnapshot = new SnapshotSpan(startPos, caretPos);
            var text = searchSnapshot.GetText();

            var indexOfCurrentClassAttribute = text.LastIndexOf("class=\"", StringComparison.InvariantCultureIgnoreCase);
            if (indexOfCurrentClassAttribute == -1)
            {
                classText = null;
                return false;
            }

            var quotationMarkAfterLastClassAttribute = text.IndexOf('\"', indexOfCurrentClassAttribute);
            var lastQuotationMark = text.LastIndexOf('\"');

            if (lastQuotationMark == quotationMarkAfterLastClassAttribute)
            {
                classText = text.Substring(lastQuotationMark + 1);
                return true;
            }
            else
            {
                classText = null;
                return false;
            }
        }

        private int CharsAfterSignificantPoint(string classText)
        {
            var textAfterSignificantPoint = classText.Split(' ', ':').Last();

            return textAfterSignificantPoint.Length;
        }

        private bool RetriggerIntellisense(string classText)
        {
            return classText != null && classText.EndsWith(":");
        }

        /// <summary>
        /// Narrow down the list of options as the user types input
        /// </summary>
        private void Filter()
        {
            if (_currentSession == null || _currentSession.SelectedCompletionSet == null)
                return;

            _currentSession?.SelectedCompletionSet?.Filter();
            _currentSession?.SelectedCompletionSet?.SelectBestMatch();
        }

        /// <summary>
        /// Dismisses the other sessions so two completion windows do not show up at once
        /// </summary>
        private void DismissOtherSessions()
        {
            foreach (var session in Broker.GetSessions(TextView))
            {
                if (session != _currentSession)
                {
                    session.Dismiss();
                }
            }
        }

        /// <summary>
        /// Cancel the auto-complete session, and leave the text unmodified
        /// </summary>
        bool Cancel()
        {
            if (_currentSession == null || _currentSession.SelectedCompletionSet == null)
                return false;

            _currentSession.Dismiss();

            return true;
        }

        /// <summary>
        /// Auto-complete text using the specified token
        /// </summary>
        bool Complete(bool force)
        {
            if (_currentSession == null || _currentSession.SelectedCompletionSet == null || _currentSession.SelectedCompletionSet.SelectionStatus == null)
            {
                return false;
            }

            if (!_currentSession.SelectedCompletionSet.SelectionStatus.IsSelected)
            {
                if (force)
                {
                    _currentSession.SelectedCompletionSet.SelectBestMatch();
                }
                else
                {
                    _currentSession.Dismiss();
                    return false;
                }
            }

            var completionText = _currentSession.SelectedCompletionSet.SelectionStatus.Completion.InsertionText;
            var moveOneBack = completionText.EndsWith("]");
            var moveTwoBack = completionText.EndsWith("]:");
            _currentSession.Commit();

            if (moveOneBack)
            {
                TextView.Caret.MoveTo(TextView.Caret.Position.BufferPosition - 1);
            }
            else if (moveTwoBack)
            {
                TextView.Caret.MoveTo(TextView.Caret.Position.BufferPosition - 2);
            }

            return true;
        }

        /// <summary>
        /// Display list of potential tokens
        /// </summary>
        bool StartSession()
        {
            if (_currentSession != null)
                return false;

            var caret = TextView.Caret.Position.Point.GetPoint(
                textBuffer => !textBuffer.ContentType.IsOfType("projection"), PositionAffinity.Predecessor);

            if (!caret.HasValue)
                return false;

            ITextSnapshot snapshot = caret.Value.Snapshot;

            var completionActive = Broker.IsCompletionActive(TextView);

            if (completionActive)
            {
                _currentSession = Broker.GetSessions(TextView)[0];
                _currentSession.Dismissed += OnSessionDismissed;
            }
            else
            {
                DismissOtherSessions();
                _currentSession = Broker.CreateCompletionSession(TextView, snapshot.CreateTrackingPoint(caret.Value.Position, PointTrackingMode.Positive), true);
                _currentSession.Dismissed += OnSessionDismissed;
                _currentSession.Start();

                // Sometimes, creating another session will have irrelevant completions, so we need to filter
                Filter();
            }

            return true;
        }

        public int QueryStatus(ref Guid pguidCmdGroup, uint cCmds, OLECMD[] prgCmds, IntPtr pCmdText)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (pguidCmdGroup == VSConstants.VSStd2K)
            {
                switch ((VSConstants.VSStd2KCmdID)prgCmds[0].cmdID)
                {
                    case VSConstants.VSStd2KCmdID.AUTOCOMPLETE:
                    case VSConstants.VSStd2KCmdID.COMPLETEWORD:
                        prgCmds[0].cmdf = (uint)OLECMDF.OLECMDF_ENABLED | (uint)OLECMDF.OLECMDF_SUPPORTED;
                        return VSConstants.S_OK;
                }
            }
            return Next.QueryStatus(pguidCmdGroup, cCmds, prgCmds, pCmdText);
        }

        private void OnSessionDismissed(object sender, EventArgs e)
        {
            _currentSession.Dismissed -= OnSessionDismissed;
            _currentSession = null;
        }
    }

    #endregion
}