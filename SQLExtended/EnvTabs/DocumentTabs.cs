using System;
using System.Collections.Generic;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace SQLExtended.EnvTabs;

/// <summary>One open document tab, as much of it as this subsystem needs.</summary>
internal sealed class DocumentTab
{
    public IVsWindowFrame Frame { get; set; }

    /// <summary>Full path of the document. This is what the shell matches colour regexes against.</summary>
    public string Path { get; set; }

    /// <summary>The caption as it stands, which may already carry a prefix we added.</summary>
    public string Caption { get; set; }

    public IVsHierarchy Hierarchy { get; set; }
    public uint ItemId { get; set; }
    public bool IsActive { get; set; }
}

/// <summary>
/// Enumerates open document tabs and renames them.
///
/// Renaming is <c>IVsWindowFrame.SetProperty</c> with a caption property, and <b>which</b> caption
/// property works is not something to assume: the three are layered (owner caption, frame caption, editor
/// caption) and a given editor honours a different one depending on how it was opened. SSMS query windows
/// are not ordinary editors, so all three are attempted in the order that works most often and the first
/// one the frame accepts wins.
/// </summary>
internal static class DocumentTabs
{
    /// <summary>
    /// All open document tabs. Frames that refuse to describe themselves are skipped rather than
    /// guessed at — a tab with no path cannot be colour-matched anyway.
    /// </summary>
    public static List<DocumentTab> Enumerate(IServiceProvider serviceProvider)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var tabs = new List<DocumentTab>();

        try
        {
            if (serviceProvider?.GetService(typeof(SVsUIShell)) is not IVsUIShell shell) return tabs;
            if (ErrorHandler.Failed(shell.GetDocumentWindowEnum(out IEnumWindowFrames frames)) || frames == null) return tabs;

            // The active document frame comes from the selection service, not the shell: IVsUIShell can
            // enumerate document windows but has no "which one is current".
            IVsWindowFrame active = null;
            try
            {
                if (serviceProvider.GetService(typeof(SVsShellMonitorSelection)) is IVsMonitorSelection selection &&
                    ErrorHandler.Succeeded(selection.GetCurrentElementValue((uint)VSConstants.VSSELELEMID.SEID_DocumentFrame, out object current)))
                    active = current as IVsWindowFrame;
            }
            catch
            {
                active = null;
            }

            var buffer = new IVsWindowFrame[1];
            while (frames.Next(1, buffer, out uint fetched) == VSConstants.S_OK && fetched == 1)
            {
                var tab = Describe(buffer[0], active);
                if (tab != null) tabs.Add(tab);
            }
        }
        catch (Exception ex)
        {
            EnvTabsDiagnostics.Note("Could not enumerate document tabs: " + ex.Message);
        }

        return tabs;
    }

    private static DocumentTab Describe(IVsWindowFrame frame, IVsWindowFrame active)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (frame == null) return null;

        try
        {
            string path = GetStringProperty(frame, (int)__VSFPROPID.VSFPROPID_pszMkDocument);
            if (string.IsNullOrWhiteSpace(path)) return null;

            string caption = GetStringProperty(frame, (int)__VSFPROPID.VSFPROPID_OwnerCaption)
                             ?? GetStringProperty(frame, (int)__VSFPROPID.VSFPROPID_Caption);

            IVsHierarchy hierarchy = null;
            uint itemId = VSConstants.VSITEMID_NIL;
            try
            {
                if (ErrorHandler.Succeeded(frame.GetProperty((int)__VSFPROPID.VSFPROPID_Hierarchy, out object rawHierarchy)))
                    hierarchy = rawHierarchy as IVsHierarchy;
                if (ErrorHandler.Succeeded(frame.GetProperty((int)__VSFPROPID.VSFPROPID_ItemID, out object rawItemId)) && rawItemId is int id)
                    itemId = (uint)id;
            }
            catch
            {
                // A frame without a hierarchy still gets a caption; only the colour needs one.
            }

            return new DocumentTab
            {
                Frame = frame,
                Path = path,
                Caption = caption ?? "",
                Hierarchy = hierarchy,
                ItemId = itemId,
                IsActive = ReferenceEquals(frame, active),
            };
        }
        catch
        {
            return null;
        }
    }

    private static string GetStringProperty(IVsWindowFrame frame, int propertyId)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            return ErrorHandler.Succeeded(frame.GetProperty(propertyId, out object value)) ? value as string : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Sets a tab's caption, trying each caption property until one is accepted. Returns true if any took.
    /// </summary>
    public static bool TrySetCaption(IVsWindowFrame frame, string caption)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (frame == null || caption == null) return false;

        int[] properties =
        {
            (int)__VSFPROPID.VSFPROPID_OwnerCaption,
            (int)__VSFPROPID.VSFPROPID_Caption,
            (int)__VSFPROPID.VSFPROPID_EditorCaption,
        };

        foreach (int property in properties)
        {
            try
            {
                if (ErrorHandler.Succeeded(frame.SetProperty(property, caption))) return true;
            }
            catch
            {
                // Try the next one.
            }
        }

        return false;
    }
}
